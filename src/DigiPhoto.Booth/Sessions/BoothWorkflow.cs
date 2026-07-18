using System.Text.Json;
using DigiPhoto.Booth.Bundles;
using DigiPhoto.Booth.Configuration;
using DigiPhoto.Booth.Data;
using DigiPhoto.Booth.Hardware;
using DigiPhoto.Booth.Storage;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Sessions;
using DigiPhoto.Contracts.Templates;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Sessions;

public sealed class BoothWorkflow(
    IDbContextFactory<BoothDbContext> contextFactory,
    ICameraAdapter camera,
    IPrinterAdapter printer,
    BoothFileStore fileStore,
    VerifiedEventBundleStore bundles,
    BoothIdentityOptions identity,
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
        if (request.SessionId == Guid.Empty || request.EventId == Guid.Empty)
        {
            throw new ArgumentException("Session and event IDs must be non-empty.");
        }

        if (identity.TenantId == Guid.Empty || identity.DeviceId == Guid.Empty)
        {
            throw new BoothWorkflowException("The booth tenant and device identity must be configured.");
        }

        var verifiedBundle = await bundles.GetLatestStartableAsync(request.EventId, cancellationToken);

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
                TenantId = verifiedBundle.Manifest.TenantId,
                DeviceId = identity.DeviceId,
                EventId = verifiedBundle.Manifest.EventId,
                EventBundleSequence = verifiedBundle.Manifest.Sequence,
                State = SessionState.Attract,
                ActiveSlot = 1,
                RetentionDays = verifiedBundle.Manifest.RetentionDays,
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

    public async Task<BoothSessionSnapshot> SelectPackageAsync(
        Guid sessionId,
        Guid packageVersionId,
        CancellationToken cancellationToken = default)
    {
        if (packageVersionId == Guid.Empty)
        {
            throw new ArgumentException("A package version ID is required.", nameof(packageVersionId));
        }

        var current = await GetAsync(sessionId, cancellationToken);
        var verifiedBundle = await bundles.GetExactAsync(
            current.EventId,
            current.EventBundleSequence,
            cancellationToken);
        var package = verifiedBundle.Manifest.Packages.SingleOrDefault(item =>
            item.VersionId == packageVersionId)
            ?? throw new BoothWorkflowException("The selected package is not in this session's verified bundle.");

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

        if (package.PrintCopies is < 1 or > 10)
        {
            throw new ArgumentOutOfRangeException(
                nameof(packageVersionId),
                "The selected package's print copies must be between 1 and 10.");
        }

        return await TransitionAsync(
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

    public async Task<BoothSessionSnapshot> AcceptPrivacyAsync(
        Guid sessionId,
        AcceptPrivacyRequest assent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assent);
        var current = await GetAsync(sessionId, cancellationToken);
        var verifiedBundle = await bundles.GetExactAsync(
            current.EventId,
            current.EventBundleSequence,
            cancellationToken);
        var notice = verifiedBundle.Manifest.PrivacyNotice;
        var privacy = new PrivacyRecord(
            notice.NoticeId,
            notice.Version,
            notice.ContentSha256,
            notice.Locale,
            LawfulBasis: "contract",
            assent.DisplayedAtUtc,
            assent.AssentedAtUtc,
            assent.AssentingAction,
            assent.ParticipantsConfirmed,
            assent.IncludesMinor,
            assent.GuardianConfirmed,
            assent.PromotionConsent,
            assent.PublicDisplayConsent);
        ValidatePrivacy(privacy);
        return await TransitionAsync(
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
            var captureRequest = camera.PlanCapture(
                session.TenantId,
                session.EventId,
                session.Id,
                Guid.NewGuid());
            var pendingCapture = new SessionMediaRow
            {
                Id = captureRequest.MediaId,
                TenantId = session.TenantId,
                SessionId = session.Id,
                Kind = MediaKind.OriginalPhoto,
                RelativePath = captureRequest.RelativePath,
                Sha256 = string.Empty,
                ByteLength = 0,
                CreatedAtUtc = clock.GetUtcNow(),
            };
            session.Media.Add(pendingCapture);
            ChangeState(session, SessionState.Countdown, SessionState.Capturing);
            await database.SaveChangesAsync(cancellationToken);

            CameraCapture capture;
            try
            {
                capture = await camera.CaptureAsync(captureRequest, cancellationToken);
            }
            catch
            {
                session.RecoveryReason = "Camera capture failed or its outcome is unknown.";
                ChangeState(session, SessionState.Capturing, SessionState.RecoveryRequired);
                await database.SaveChangesAsync(CancellationToken.None);
                throw;
            }

            pendingCapture.RelativePath = capture.RelativePath;
            pendingCapture.Sha256 = capture.Sha256;
            pendingCapture.ByteLength = capture.ByteLength;
            pendingCapture.WidthPx = capture.WidthPx;
            pendingCapture.HeightPx = capture.HeightPx;
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

            var verifiedBundle = await bundles.GetExactAsync(
                session.EventId,
                session.EventBundleSequence,
                cancellationToken);
            var package = verifiedBundle.Manifest.Packages.Single(item =>
                item.VersionId == session.PackageVersionId);
            var profile = PrintProfiles.Get(package.PrintLayout);

            var stored = await fileStore.WriteRenderPngAsync(
                $"tenants/{session.TenantId:N}/events/{session.EventId:N}/sessions/{sessionId:N}/output/{sessionId:N}.png",
                png,
                profile.SheetWidthPx,
                profile.SheetHeightPx,
                cancellationToken);

            var existing = session.Media.SingleOrDefault(item => item.Kind == MediaKind.PrintComposite);
            if (existing is null)
            {
                session.Media.Add(new SessionMediaRow
                {
                    Id = session.Id,
                    TenantId = session.TenantId,
                    SessionId = session.Id,
                    Kind = MediaKind.PrintComposite,
                    RelativePath = stored.RelativePath,
                    Sha256 = stored.Sha256,
                    ByteLength = stored.ByteLength,
                    WidthPx = profile.SheetWidthPx,
                    HeightPx = profile.SheetHeightPx,
                    CreatedAtUtc = clock.GetUtcNow(),
                });
            }
            else
            {
                existing.RelativePath = stored.RelativePath;
                existing.Sha256 = stored.Sha256;
                existing.ByteLength = stored.ByteLength;
                existing.WidthPx = profile.SheetWidthPx;
                existing.HeightPx = profile.SheetHeightPx;
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
            var persistedOutput = await fileStore.InspectAsync(output.RelativePath, cancellationToken);
            if (persistedOutput is null || persistedOutput.ByteLength != output.ByteLength ||
                !string.Equals(persistedOutput.Sha256, output.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                session.RecoveryReason = "The final output is missing or failed its SHA-256 check.";
                ChangeState(session, SessionState.PrintPending, SessionState.RecoveryRequired);
                await database.SaveChangesAsync(cancellationToken);
                throw new BoothWorkflowException(
                    "The final output is missing or corrupt; nothing was sent to the printer.");
            }

            var now = clock.GetUtcNow();
            var job = new PrintJobRow
            {
                Id = Guid.NewGuid(),
                TenantId = session.TenantId,
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
                session.RecoveryReason = "Printer submission failed with an unknown physical outcome.";
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
                session.RecoveryReason = outcome == PrinterSubmissionOutcome.Ambiguous
                    ? "The printer may have accepted the job; it will not be submitted again automatically."
                    : "The printer rejected the job; operator review is required.";
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
        if (!Enum.IsDefined(resolution))
        {
            throw new ArgumentOutOfRangeException(nameof(resolution));
        }

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
                session.RecoveryReason = "Operator confirmed that the ambiguous print completed.";
                ChangeState(session, SessionState.RecoveryRequired, SessionState.Completed);
                session.CompletedAtUtc = clock.GetUtcNow();
            }
            else
            {
                printJob.State = PrintJobState.Failed;
                session.RecoveryReason = "Operator cancelled the session without another print attempt.";
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

    public async Task<BoothSessionSnapshot> CancelRecoveryAsync(
        Guid sessionId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var normalizedReason = reason.Trim();
        if (normalizedReason.Length is < 3 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(reason), "Recovery reason must be 3 to 500 characters.");
        }

        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var session = await FindAsync(database, sessionId, cancellationToken);
            EnsureActive(session);
            EnsureState(session, SessionState.RecoveryRequired);
            session.RecoveryReason = normalizedReason;
            ChangeState(session, SessionState.RecoveryRequired, SessionState.Cancelled);
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
                var pendingCapture = session.Media.SingleOrDefault(item =>
                    item.Kind == MediaKind.OriginalPhoto && item.Sha256.Length == 0);
                var stored = pendingCapture is null
                    ? null
                    : await fileStore.InspectAsync(pendingCapture.RelativePath, cancellationToken);
                if (pendingCapture is not null && stored is not null)
                {
                    pendingCapture.Sha256 = stored.Sha256;
                    pendingCapture.ByteLength = stored.ByteLength;
                    ChangeState(session, SessionState.Capturing, SessionState.Review);
                }
                else
                {
                    session.RecoveryReason = "Capture was interrupted before a complete original was persisted.";
                    ChangeState(session, SessionState.Capturing, SessionState.RecoveryRequired);
                }
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

                    session.RecoveryReason =
                        "The booth restarted during printer submission; the job will not be sent again automatically.";
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
        if (privacy.NoticeId == Guid.Empty || privacy.NoticeVersion <= 0 ||
            string.IsNullOrWhiteSpace(privacy.NoticeSha256) || privacy.NoticeSha256.Length != 64 ||
            !privacy.NoticeSha256.All(Uri.IsHexDigit) || string.IsNullOrWhiteSpace(privacy.Locale) ||
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
            .Where(item => item.Sha256.Length == 64 && item.ByteLength > 0)
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
            session.RecoveryReason,
            Payment: null,
            printJob,
            media,
            session.RetentionDays,
            session.StartedAtUtc,
            session.UpdatedAtUtc,
            session.CompletedAtUtc);
    }
}
