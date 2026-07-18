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

public sealed record CameraCaptureRequest(
    Guid TenantId,
    Guid EventId,
    Guid SessionId,
    Guid MediaId,
    string RelativePath);

public interface ICameraAdapter
{
    CameraCaptureRequest PlanCapture(Guid tenantId, Guid eventId, Guid sessionId, Guid mediaId);

    Task<CameraCapture> CaptureAsync(
        CameraCaptureRequest request,
        CancellationToken cancellationToken);
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
    public CameraCaptureRequest PlanCapture(
        Guid tenantId,
        Guid eventId,
        Guid sessionId,
        Guid mediaId) =>
        new(
            tenantId,
            eventId,
            sessionId,
            mediaId,
            $"tenants/{tenantId:N}/events/{eventId:N}/sessions/{sessionId:N}/originals/{mediaId:N}.simulated.ppm");

    public async Task<CameraCapture> CaptureAsync(
        CameraCaptureRequest request,
        CancellationToken cancellationToken)
    {
        var bytes = Encoding.ASCII.GetBytes(
            $"P3\n# SYNTHETIC SIMULATED CAMERA CAPTURE {request.MediaId:N}\n2 2\n255\n80 80 80 220 220 220\n220 220 220 80 80 80\n");
        var stored = await fileStore.WriteBytesAsync(request.RelativePath, bytes, cancellationToken);

        return new CameraCapture(
            request.MediaId,
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
