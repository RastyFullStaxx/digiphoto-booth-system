using System.Text.Json;
using DigiPhoto.Booth.Data;
using DigiPhoto.Booth.Hardware;
using DigiPhoto.Booth.Storage;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Sessions;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Sessions;

public sealed class BoothWorkflow(
    IDbContextFactory<BoothDbContext> contextFactory,
    ICameraAdapter camera,
    IPrinterAdapter printer,
    BoothFileStore fileStore,
    TimeProvider clock) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    public void Dispose() => _stateGate.Dispose();

    public async Task<BoothSessionSnapshot?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var session = await SessionQuery(database)
            .SingleOrDefaultAsync(row => row.ActiveSlot == 1, cancellationToken);
        return session is null ? null : Map(session);
    }

    public async Task<BoothSessionSnapshot> GetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        return Map(await FindAsync(database, sessionId, cancellationToken));
    }

    public async Task<BoothSessionSnapshot> StartAsync(
        StartSessionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.SessionId == Guid.Empty || request.TenantId == Guid.Empty ||
            request.DeviceId == Guid.Empty || request.EventId == Guid.Empty)
        {
            throw new ArgumentException("Session, tenant, device, and event IDs must be non-empty.");
        }

        if (request.EventBundleSequence <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Event bundle sequence must be positive.");
        }

        if (request.RetentionDays is not (7 or 30 or 90))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Retention must be 7, 30, or 90 days.");
        }

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            if (await database.Sessions.AnyAsync(row => row.ActiveSlot == 1, cancellationToken))
            {
                throw new BoothWorkflowException("The booth already has an active guest session.");
            }

            var now = clock.GetUtcNow();
            var session = new BoothSessionRow
            {
                Id = request.SessionId,
                TenantId = request.TenantId,
                DeviceId = request.DeviceId,
                EventId = request.EventId,
                EventBundleSequence = request.EventBundleSequence,
                State = SessionState.Attract,
                ActiveSlot = 1,
                RetentionDays = request.RetentionDays,
                StartedAtUtc = now,
                UpdatedAtUtc = now,
            };
            database.Sessions.Add(session);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception)
            {
                throw new BoothWorkflowException("The booth already has an active guest session.", exception);
            }

            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public Task<BoothSessionSnapshot> BeginPackageSelectionAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(sessionId, SessionState.Attract, SessionState.PackageSelection, null, cancellationToken);

    public Task<BoothSessionSnapshot> SelectPackageAsync(
        Guid sessionId,
        PackageSnapshot package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);
        if (package.MediaMode != MediaMode.Photo)
        {
            throw new BoothWorkflowException("This simulator slice supports the photo package only.");
        }

        if (package.Price.MinorUnits != 0)
        {
            throw new BoothWorkflowException(
                "Paid packages remain locked until cloud-verified payment is implemented.");
        }

        if (package.RequiredShots != 1)
        {
            throw new BoothWorkflowException("This first simulator slice supports exactly one required shot.");
        }

        if (package.PrintCopies <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(package), "Print copies must be positive.");
        }

        return TransitionAsync(
            sessionId,
            SessionState.PackageSelection,
            SessionState.PrivacyNotice,
            session =>
            {
                session.PackageVersionId = package.VersionId;
                session.TemplateVersionId = package.TemplateVersionId;
                session.RequiredShots = package.RequiredShots;
                session.PrintCopies = package.PrintCopies;
            },
            cancellationToken);
    }

    public Task<BoothSessionSnapshot> AcceptPrivacyAsync(
        Guid sessionId,
        PrivacyRecord privacy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(privacy);
        ValidatePrivacy(privacy);
        return TransitionAsync(
            sessionId,
            SessionState.PrivacyNotice,
            SessionState.LivePreview,
            session => session.PrivacyJson = JsonSerializer.Serialize(privacy, JsonOptions),
            cancellationToken);
    }

    public Task<BoothSessionSnapshot> StartCountdownAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(sessionId, SessionState.LivePreview, SessionState.Countdown, null, cancellationToken);

    public async Task<BoothSessionSnapshot> CaptureAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            ChangeState(session, SessionState.Countdown, SessionState.Capturing);
            await database.SaveChangesAsync(cancellationToken);

            CameraCapture capture;
            try
            {
                capture = await camera.CaptureAsync(sessionId, cancellationToken);
            }
            catch
            {
                ChangeState(session, SessionState.Capturing, SessionState.RecoveryRequired);
                await database.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            session.Media.Add(new SessionMediaRow
            {
                Id = capture.MediaId,
                SessionId = session.Id,
                Kind = MediaKind.OriginalPhoto,
                RelativePath = capture.RelativePath,
                Sha256 = capture.Sha256,
                ByteLength = capture.ByteLength,
                WidthPx = capture.WidthPx,
                HeightPx = capture.HeightPx,
                CreatedAtUtc = clock.GetUtcNow(),
            });
            ChangeState(session, SessionState.Capturing, SessionState.Review);
            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public Task<BoothSessionSnapshot> AcceptReviewAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default) =>
        TransitionAsync(sessionId, SessionState.Review, SessionState.Rendering, null, cancellationToken);

    public async Task<BoothSessionSnapshot> PersistRenderAsync(
        Guid sessionId,
        Stream png,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(png);
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            EnsureState(session, SessionState.Rendering);

            var stored = await fileStore.WriteRenderAsync(
                $"sessions/{sessionId:N}/output/{sessionId:N}.png",
                png,
                cancellationToken);

            var existing = session.Media.SingleOrDefault(item => item.Kind == MediaKind.PrintComposite);
            if (existing is null)
            {
                session.Media.Add(new SessionMediaRow
                {
                    Id = session.Id,
                    SessionId = session.Id,
                    Kind = MediaKind.PrintComposite,
                    RelativePath = stored.RelativePath,
                    Sha256 = stored.Sha256,
                    ByteLength = stored.ByteLength,
                    WidthPx = 1200,
                    HeightPx = 1800,
                    CreatedAtUtc = clock.GetUtcNow(),
                });
            }
            else
            {
                existing.RelativePath = stored.RelativePath;
                existing.Sha256 = stored.Sha256;
                existing.ByteLength = stored.ByteLength;
                existing.CreatedAtUtc = clock.GetUtcNow();
            }

            ChangeState(session, SessionState.Rendering, SessionState.PrintPending);
            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<BoothSessionSnapshot> SubmitPrintAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);

            if (session.PrintJob is not null)
            {
                return Map(session);
            }

            EnsureState(session, SessionState.PrintPending);
            var output = session.Media.SingleOrDefault(item => item.Kind == MediaKind.PrintComposite)
                ?? throw new BoothWorkflowException("A persisted print composite is required before printing.");
            var now = clock.GetUtcNow();
            var job = new PrintJobRow
            {
                Id = Guid.NewGuid(),
                SessionId = session.Id,
                State = PrintJobState.Pending,
                RequestedCopies = session.PrintCopies,
                IdempotencyKey = $"session:{session.Id:N}:initial-print",
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };
            session.PrintJob = job;
            ChangeState(session, SessionState.PrintPending, SessionState.Printing);
            await database.SaveChangesAsync(cancellationToken);

            PrinterSubmissionOutcome outcome;
            try
            {
                outcome = await printer.SubmitAsync(
                    new PrintSubmission(
                        job.Id,
                        session.Id,
                        job.IdempotencyKey,
                        output.RelativePath,
                        job.RequestedCopies),
                    cancellationToken);
            }
            catch
            {
                job.State = PrintJobState.Ambiguous;
                job.UpdatedAtUtc = clock.GetUtcNow();
                ChangeState(session, SessionState.Printing, SessionState.RecoveryRequired);
                await database.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            job.SubmittedAtUtc = clock.GetUtcNow();
            job.UpdatedAtUtc = job.SubmittedAtUtc.Value;
            if (outcome == PrinterSubmissionOutcome.Completed)
            {
                job.State = PrintJobState.Completed;
                ChangeState(session, SessionState.Printing, SessionState.Completed);
                session.CompletedAtUtc = clock.GetUtcNow();
            }
            else
            {
                job.State = outcome == PrinterSubmissionOutcome.Ambiguous
                    ? PrintJobState.Ambiguous
                    : PrintJobState.Failed;
                ChangeState(session, SessionState.Printing, SessionState.RecoveryRequired);
            }

            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<BoothSessionSnapshot> ResolvePrintAsync(
        Guid sessionId,
        PrintResolution resolution,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            EnsureState(session, SessionState.RecoveryRequired);
            var printJob = session.PrintJob
                ?? throw new BoothWorkflowException("The session has no print job to resolve.");
            if (printJob.State is not (PrintJobState.Ambiguous or PrintJobState.Failed))
            {
                throw new BoothWorkflowException("Only an ambiguous or failed print can be resolved.");
            }

            if (resolution == PrintResolution.ConfirmedPrinted)
            {
                printJob.State = PrintJobState.Completed;
                ChangeState(session, SessionState.RecoveryRequired, SessionState.Completed);
                session.CompletedAtUtc = clock.GetUtcNow();
            }
            else
            {
                printJob.State = PrintJobState.Failed;
                ChangeState(session, SessionState.RecoveryRequired, SessionState.Cancelled);
            }

            printJob.UpdatedAtUtc = clock.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<BoothSessionSnapshot> ResetAsync(
        Guid sessionId,
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            if (session.State is not (SessionState.Completed or SessionState.Cancelled))
            {
                throw new BoothWorkflowException("Only a completed or cancelled session can be reset.");
            }

            session.ActiveSlot = null;
            session.UpdatedAtUtc = clock.GetUtcNow();
            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    public async Task<BoothSessionSnapshot?> RecoverActiveAsync(
        CancellationToken cancellationToken = default)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await SessionQuery(database)
                .SingleOrDefaultAsync(row => row.ActiveSlot == 1, cancellationToken);
            if (session is null)
            {
                return null;
            }

            if (session.State == SessionState.Capturing)
            {
                ChangeState(session, SessionState.Capturing, SessionState.RecoveryRequired);
            }
            else if (session.State == SessionState.Printing)
            {
                if (session.PrintJob?.State == PrintJobState.Completed)
                {
                    ChangeState(session, SessionState.Printing, SessionState.Completed);
                    session.CompletedAtUtc ??= clock.GetUtcNow();
                }
                else
                {
                    if (session.PrintJob is not null)
                    {
                        session.PrintJob.State = PrintJobState.Ambiguous;
                        session.PrintJob.UpdatedAtUtc = clock.GetUtcNow();
                    }

                    ChangeState(session, SessionState.Printing, SessionState.RecoveryRequired);
                }
            }

            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private async Task<BoothSessionSnapshot> TransitionAsync(
        Guid sessionId,
        SessionState expected,
        SessionState next,
        Action<BoothSessionRow>? update,
        CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            ChangeState(session, expected, next);
            update?.Invoke(session);
            await database.SaveChangesAsync(cancellationToken);
            return Map(session);
        }
        finally
        {
            _stateGate.Release();
        }
    }

    private void ChangeState(BoothSessionRow session, SessionState expected, SessionState next)
    {
        EnsureState(session, expected);
        SessionStateMachine.EnsureAllowed(expected, next);
        session.State = next;
        session.UpdatedAtUtc = clock.GetUtcNow();
    }

    private static void EnsureState(BoothSessionRow session, SessionState expected)
    {
        if (session.State != expected)
        {
            throw new BoothWorkflowException(
                $"Session {session.Id} is {session.State}; expected {expected}.");
        }
    }

    private static void EnsureActive(BoothSessionRow session)
    {
        if (session.ActiveSlot != 1)
        {
            throw new BoothWorkflowException("The session is no longer active on this booth.");
        }
    }

    private static void ValidatePrivacy(PrivacyRecord privacy)
    {
        if (privacy.NoticeId == Guid.Empty || string.IsNullOrWhiteSpace(privacy.NoticeSha256) ||
            privacy.NoticeSha256.Length != 64 || string.IsNullOrWhiteSpace(privacy.Locale) ||
            string.IsNullOrWhiteSpace(privacy.LawfulBasis) ||
            string.IsNullOrWhiteSpace(privacy.AssentingAction))
        {
            throw new ArgumentException("Privacy notice identity, hash, locale, basis, and action are required.");
        }

        if (!privacy.ParticipantsConfirmed || privacy.AssentedAtUtc < privacy.DisplayedAtUtc)
        {
            throw new ArgumentException("Privacy assent must follow display and confirm all participants.");
        }

        if (privacy.IncludesMinor && !privacy.GuardianConfirmed)
        {
            throw new ArgumentException("A minor session requires guardian confirmation.");
        }
    }

    private static IQueryable<BoothSessionRow> SessionQuery(BoothDbContext database) =>
        database.Sessions
            .Include(row => row.Media)
            .Include(row => row.PrintJob);

    private static async Task<BoothSessionRow> FindAsync(
        BoothDbContext database,
        Guid sessionId,
        CancellationToken cancellationToken) =>
        await SessionQuery(database).SingleOrDefaultAsync(row => row.Id == sessionId, cancellationToken)
        ?? throw new KeyNotFoundException($"Session {sessionId} was not found.");

    private static BoothSessionSnapshot Map(BoothSessionRow session)
    {
        var privacy = session.PrivacyJson is null
            ? null
            : JsonSerializer.Deserialize<PrivacyRecord>(session.PrivacyJson, JsonOptions);
        var media = session.Media
            .OrderBy(item => item.CreatedAtUtc)
            .Select(item => new MediaInventoryItem(
                item.Id,
                item.Kind,
                item.RelativePath,
                item.Sha256,
                item.ByteLength,
                item.WidthPx,
                item.HeightPx,
                item.CreatedAtUtc))
            .ToArray();
        var printJob = session.PrintJob is null
            ? null
            : new PrintJobReference(
                session.PrintJob.Id,
                session.PrintJob.State,
                session.PrintJob.RequestedCopies,
                session.PrintJob.IdempotencyKey,
                session.PrintJob.SubmittedAtUtc);

        return new BoothSessionSnapshot(
            ContractVersions.SessionManifest,
            session.Id,
            session.TenantId,
            session.DeviceId,
            session.EventId,
            session.PackageVersionId,
            session.TemplateVersionId,
            session.EventBundleSequence,
            session.State,
            session.ActiveSlot == 1,
            session.RequiredShots,
            media.Count(item => item.Kind == MediaKind.OriginalPhoto),
            session.PrintCopies,
            privacy,
            Payment: null,
            printJob,
            media,
            session.RetentionDays,
            session.StartedAtUtc,
            session.UpdatedAtUtc,
            session.CompletedAtUtc);
    }
}
