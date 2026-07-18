using System.Collections.Concurrent;
using System.Text;
using DigiPhoto.Booth.Storage;

namespace DigiPhoto.Booth.Hardware;

public sealed record CameraCapture(
    Guid MediaId,
    string RelativePath,
    string Sha256,
    long ByteLength,
    int WidthPx,
    int HeightPx);

public interface ICameraAdapter
{
    Task<CameraCapture> CaptureAsync(Guid sessionId, CancellationToken cancellationToken);
}

public enum PrinterSubmissionOutcome
{
    Completed,
    Ambiguous,
    Failed,
}

public sealed record PrintSubmission(
    Guid PrintJobId,
    Guid SessionId,
    string IdempotencyKey,
    string RelativeOutputPath,
    int Copies);

public interface IPrinterAdapter
{
    Task<PrinterSubmissionOutcome> SubmitAsync(
        PrintSubmission submission,
        CancellationToken cancellationToken);
}

public sealed class SimulatedCameraAdapter(BoothFileStore fileStore) : ICameraAdapter
{
    public async Task<CameraCapture> CaptureAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var mediaId = Guid.NewGuid();
        var relativePath = $"sessions/{sessionId:N}/originals/{mediaId:N}.simulated.ppm";
        var bytes = Encoding.ASCII.GetBytes(
            $"P3\n# SYNTHETIC SIMULATED CAMERA CAPTURE {mediaId:N}\n2 2\n255\n80 80 80 220 220 220\n220 220 220 80 80 80\n");
        var stored = await fileStore.WriteBytesAsync(relativePath, bytes, cancellationToken);

        return new CameraCapture(
            mediaId,
            stored.RelativePath,
            stored.Sha256,
            stored.ByteLength,
            WidthPx: 2,
            HeightPx: 2);
    }
}

public sealed class SimulatedPrinterAdapter : IPrinterAdapter, IDisposable
{
    private readonly SemaphoreSlim _queue = new(1, 1);
    private readonly ConcurrentQueue<PrinterSubmissionOutcome> _outcomes = new();
    private int _activeCalls;
    private int _callCount;
    private int _maximumConcurrency;

    public int CallCount => Volatile.Read(ref _callCount);

    public int MaximumConcurrency => Volatile.Read(ref _maximumConcurrency);

    public void QueueOutcome(PrinterSubmissionOutcome outcome) => _outcomes.Enqueue(outcome);

    public void Dispose() => _queue.Dispose();

    public async Task<PrinterSubmissionOutcome> SubmitAsync(
        PrintSubmission submission,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.IdempotencyKey);
        await _queue.WaitAsync(cancellationToken);
        try
        {
            Interlocked.Increment(ref _callCount);
            var active = Interlocked.Increment(ref _activeCalls);
            UpdateMaximumConcurrency(active);
            await Task.Yield();
            return _outcomes.TryDequeue(out var outcome)
                ? outcome
                : PrinterSubmissionOutcome.Completed;
        }
        finally
        {
            Interlocked.Decrement(ref _activeCalls);
            _queue.Release();
        }
    }

    private void UpdateMaximumConcurrency(int value)
    {
        while (true)
        {
            var current = Volatile.Read(ref _maximumConcurrency);
            if (value <= current ||
                Interlocked.CompareExchange(ref _maximumConcurrency, value, current) == current)
            {
                return;
            }
        }
    }
}
