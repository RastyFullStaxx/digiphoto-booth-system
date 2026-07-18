using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using DigiPhoto.Booth.Bundles;
using DigiPhoto.Booth.Configuration;
using DigiPhoto.Booth.Data;
using DigiPhoto.Booth.Hardware;
using DigiPhoto.Booth.Sessions;
using DigiPhoto.Booth.Storage;
using DigiPhoto.Cloud.Events;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Sessions;
using DigiPhoto.Contracts.Templates;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Tests;

public sealed class BoothWorkflowTests
{
    [Fact]
    public async Task CloudSignedBundleIsAcceptedByTheBoothVerifier()
    {
        await using var booth = await TestBooth.CreateAsync();

        var verified = await booth.BundleStore.GetLatestStartableAsync(booth.EventId);

        Assert.Equal(booth.TenantId, verified.Manifest.TenantId);
        Assert.Equal(booth.EventId, verified.Manifest.EventId);
        Assert.True(DevelopmentBundleSigner.Verify(
            verified.Manifest,
            verified.Signature,
            new DevelopmentSigningKey(
                booth.SigningKey.Algorithm,
                booth.SigningKey.KeyId,
                booth.SigningKey.SubjectPublicKeyInfoBase64)));
    }

    [Fact]
    public async Task RelabeledPersistedBundleCannotServeAnotherEvent()
    {
        await using var booth = await TestBooth.CreateAsync();
        var relabeledEventId = Guid.NewGuid();
        await booth.RelabelPersistedBundleAsync(relabeledEventId);

        await Assert.ThrowsAsync<EventBundleVerificationException>(() =>
            booth.BundleStore.GetLatestStartableAsync(relabeledEventId));
        await Assert.ThrowsAsync<EventBundleVerificationException>(() =>
            booth.BundleStore.GetExactAsync(relabeledEventId, sequence: 1));
    }

    [Theory]
    [InlineData(SessionState.Attract, SessionState.PackageSelection)]
    [InlineData(SessionState.PackageSelection, SessionState.PrivacyNotice)]
    [InlineData(SessionState.PrivacyNotice, SessionState.LivePreview)]
    [InlineData(SessionState.LivePreview, SessionState.Countdown)]
    [InlineData(SessionState.Countdown, SessionState.Capturing)]
    [InlineData(SessionState.Capturing, SessionState.Review)]
    [InlineData(SessionState.Review, SessionState.Rendering)]
    [InlineData(SessionState.Rendering, SessionState.PrintPending)]
    [InlineData(SessionState.PrintPending, SessionState.Printing)]
    [InlineData(SessionState.Printing, SessionState.Completed)]
    public void StateMachineAllowsTheFreePhotoPath(SessionState current, SessionState next)
    {
        Assert.True(SessionStateMachine.CanTransition(current, next));
    }

    [Fact]
    public async Task InvalidTransitionDoesNotChangePersistedState()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();

        await Assert.ThrowsAsync<BoothWorkflowException>(
            () => booth.Workflow.CaptureAsync(session.SessionId));

        Assert.Equal(SessionState.Attract, (await booth.Workflow.GetAsync(session.SessionId)).State);
    }

    [Fact]
    public async Task SessionIdentityAndConfigurationComeFromTheVerifiedBundleAndBooth()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();

        Assert.Equal(booth.TenantId, session.TenantId);
        Assert.Equal(booth.DeviceId, session.DeviceId);
        Assert.Equal(booth.EventId, session.EventId);
        Assert.Equal(1, session.EventBundleSequence);
        Assert.Equal(30, session.RetentionDays);

        await booth.Workflow.BeginPackageSelectionAsync(session.SessionId);
        await booth.Workflow.SelectPackageAsync(session.SessionId, booth.PackageVersionId);
        var privacy = await booth.Workflow.AcceptPrivacyAsync(
            session.SessionId,
            TestBooth.AcceptedPrivacy());

        Assert.Equal(booth.NoticeId, privacy.Privacy?.NoticeId);
        Assert.Equal(new string('a', 64), privacy.Privacy?.NoticeSha256);
    }

    [Fact]
    public async Task BoothAllowsOnlyOneActiveSessionUntilReset()
    {
        await using var booth = await TestBooth.CreateAsync();
        var first = await booth.StartAsync();

        await Assert.ThrowsAsync<BoothWorkflowException>(() => booth.StartAsync());

        await booth.AdvanceToPrintPendingAsync(first.SessionId);
        await booth.Workflow.SubmitPrintAsync(first.SessionId);
        var reset = await booth.Workflow.ResetAsync(first.SessionId);
        var second = await booth.StartAsync();

        Assert.False(reset.IsActive);
        Assert.True(second.IsActive);
        Assert.NotEqual(first.SessionId, second.SessionId);
    }

    [Fact]
    public async Task RestartRecoversPersistedSafeStateWithoutReplayingHardware()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToCountdownAsync(session.SessionId);

        using var restarted = booth.CreateWorkflow();
        var recovered = await restarted.RecoverActiveAsync();

        Assert.NotNull(recovered);
        Assert.Equal(SessionState.Countdown, recovered.State);
        Assert.Equal(0, booth.Printer.CallCount);
        Assert.DoesNotContain(recovered.Media, item => item.Kind == MediaKind.OriginalPhoto);
    }

    [Fact]
    public async Task RestartRecoversAnOriginalWrittenBeforeMetadataCommit()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToCountdownAsync(session.SessionId);
        await booth.SeedCapturedFileBeforeMetadataCommitAsync(session.SessionId);

        using var restarted = booth.CreateWorkflow();
        var recovered = await restarted.RecoverActiveAsync();

        Assert.NotNull(recovered);
        var original = Assert.Single(recovered.Media, item => item.Kind == MediaKind.OriginalPhoto);
        Assert.Equal(SessionState.Review, recovered.State);
        Assert.Equal(64, original.Sha256.Length);
        Assert.True(original.ByteLength > 0);
    }

    [Fact]
    public async Task PaidPackageFailsClosedAndDoesNotAdvance()
    {
        await using var booth = await TestBooth.CreateAsync(priceMinor: 10_000);
        var session = await booth.StartAsync();
        await booth.Workflow.BeginPackageSelectionAsync(session.SessionId);

        await Assert.ThrowsAsync<BoothWorkflowException>(() =>
            booth.Workflow.SelectPackageAsync(session.SessionId, booth.PackageVersionId));

        var unchanged = await booth.Workflow.GetAsync(session.SessionId);
        Assert.Equal(SessionState.PackageSelection, unchanged.State);
        Assert.Null(unchanged.Payment);
    }

    [Fact]
    public async Task BundleStoreRejectsTamperingAndRollback()
    {
        await using var booth = await TestBooth.CreateAsync();
        var sequenceTwo = booth.CreateBundle(sequence: 2);
        var tampered = sequenceTwo with
        {
            Manifest = sequenceTwo.Manifest with { RetentionDays = 7 },
        };

        await Assert.ThrowsAsync<EventBundleVerificationException>(() =>
            booth.BundleStore.LoadAsync(tampered));

        await booth.BundleStore.LoadAsync(sequenceTwo);
        var rollback = booth.CreateBundle(sequence: 1);
        await Assert.ThrowsAsync<EventBundleVerificationException>(() =>
            booth.BundleStore.LoadAsync(rollback));
    }

    [Fact]
    public async Task BundleStoreRejectsAValidSignatureForAnotherTenant()
    {
        await using var booth = await TestBooth.CreateAsync();
        var foreignBundle = booth.CreateBundle(sequence: 2, tenantId: Guid.NewGuid());

        await Assert.ThrowsAsync<EventBundleVerificationException>(() =>
            booth.BundleStore.LoadAsync(foreignBundle));
    }

    [Fact]
    public async Task BundleVersionsKeepSameNamedAssetsIsolated()
    {
        var firstAsset = new byte[] { 1, 2, 3, 4 };
        var secondAsset = new byte[] { 9, 8, 7, 6 };
        await using var booth = await TestBooth.CreateAsync(assetContent: firstAsset);
        var sequenceTwo = booth.CreateBundle(sequence: 2, assetContent: secondAsset);
        await booth.StageAssetsAsync(sequenceTwo, secondAsset);

        await booth.BundleStore.LoadAsync(sequenceTwo);
        var oldBundle = await booth.BundleStore.GetExactAsync(booth.EventId, sequence: 1);
        var newBundle = await booth.BundleStore.GetExactAsync(booth.EventId, sequence: 2);

        Assert.NotEqual(oldBundle.Manifest.BundleId, newBundle.Manifest.BundleId);
        Assert.NotEqual(
            oldBundle.Manifest.Assets.Single().Sha256,
            newBundle.Manifest.Assets.Single().Sha256);
    }

    [Fact]
    public async Task PackageOutsideTheVerifiedBundleDoesNotAdvance()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.Workflow.BeginPackageSelectionAsync(session.SessionId);

        await Assert.ThrowsAsync<BoothWorkflowException>(() =>
            booth.Workflow.SelectPackageAsync(session.SessionId, Guid.NewGuid()));

        Assert.Equal(
            SessionState.PackageSelection,
            (await booth.Workflow.GetAsync(session.SessionId)).State);
    }

    [Fact]
    public async Task RestartMarksAnInflightPrintAmbiguousWithoutSubmittingIt()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);
        await booth.SeedInflightPrintAsync(session.SessionId);

        using var restarted = booth.CreateWorkflow();
        var recovered = await restarted.RecoverActiveAsync();

        Assert.NotNull(recovered);
        Assert.Equal(SessionState.RecoveryRequired, recovered.State);
        Assert.Equal(PrintJobState.Ambiguous, recovered.PrintJob?.State);
        Assert.Equal(0, booth.Printer.CallCount);
    }

    [Fact]
    public async Task DuplicatePrintCommandReturnsTheSameCompletedJob()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);

        var first = await booth.Workflow.SubmitPrintAsync(session.SessionId);
        var duplicate = await booth.Workflow.SubmitPrintAsync(session.SessionId);

        Assert.Equal(SessionState.Completed, first.State);
        Assert.Equal(first.PrintJob?.PrintJobId, duplicate.PrintJob?.PrintJobId);
        Assert.Equal(first.PrintJob?.IdempotencyKey, duplicate.PrintJob?.IdempotencyKey);
        Assert.Equal(1, booth.Printer.CallCount);
    }

    [Fact]
    public async Task MissingOrCorruptOutputNeverReachesThePrinter()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);
        await booth.CorruptOutputAsync(session.SessionId);

        await Assert.ThrowsAsync<BoothWorkflowException>(() =>
            booth.Workflow.SubmitPrintAsync(session.SessionId));

        var failed = await booth.Workflow.GetAsync(session.SessionId);
        Assert.Equal(SessionState.RecoveryRequired, failed.State);
        Assert.Contains("SHA-256", failed.RecoveryReason, StringComparison.Ordinal);
        Assert.Equal(0, booth.Printer.CallCount);
    }

    [Fact]
    public async Task RenderRejectsPngWithWrongProfileDimensions()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToRenderingAsync(session.SessionId);

        await Assert.ThrowsAsync<ArgumentException>(() => booth.Workflow.PersistRenderAsync(
            session.SessionId,
            new MemoryStream(TestBooth.CreatePng(width: 1, height: 1), writable: false)));

        var unchanged = await booth.Workflow.GetAsync(session.SessionId);
        Assert.Equal(SessionState.Rendering, unchanged.State);
        Assert.DoesNotContain(unchanged.Media, item => item.Kind == MediaKind.PrintComposite);
    }

    [Fact]
    public async Task RenderRejectsHeaderOnlyPngWithExpectedProfileDimensions()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToRenderingAsync(session.SessionId);
        var headerOnly = TestBooth.CreatePng(1200, 1800)[..33];

        await Assert.ThrowsAsync<ArgumentException>(() => booth.Workflow.PersistRenderAsync(
            session.SessionId,
            new MemoryStream(headerOnly, writable: false)));

        var unchanged = await booth.Workflow.GetAsync(session.SessionId);
        Assert.Equal(SessionState.Rendering, unchanged.State);
        Assert.DoesNotContain(unchanged.Media, item => item.Kind == MediaKind.PrintComposite);
    }

    [Fact]
    public async Task PersistedMediaPathsIncludeTenantEventAndSessionBoundaries()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);

        var persisted = await booth.Workflow.GetAsync(session.SessionId);
        var expectedPrefix =
            $"tenants/{booth.TenantId:N}/events/{booth.EventId:N}/sessions/{session.SessionId:N}/";

        Assert.NotEmpty(persisted.Media);
        Assert.All(persisted.Media, item =>
            Assert.StartsWith(expectedPrefix, item.RelativePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task MediaAndPrintRecordsAreTenantScopedAndRejectTenantMismatch()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);
        await booth.Workflow.SubmitPrintAsync(session.SessionId);

        var owned = await booth.CountTenantOwnedRecordsAsync(session.SessionId);

        Assert.Equal(2, owned.MediaCount);
        Assert.Equal(1, owned.PrintJobCount);
        Assert.Equal(0, owned.ForeignTenantRecordCount);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            booth.InsertMismatchedTenantMediaAsync(session.SessionId));
    }

    [Fact]
    public async Task AmbiguousPrintRequiresOperatorResolutionAndNeverAutoReprints()
    {
        await using var booth = await TestBooth.CreateAsync();
        booth.Printer.QueueOutcome(PrinterSubmissionOutcome.Ambiguous);
        var session = await booth.StartAsync();
        await booth.AdvanceToPrintPendingAsync(session.SessionId);

        var ambiguous = await booth.Workflow.SubmitPrintAsync(session.SessionId);
        var duplicate = await booth.Workflow.SubmitPrintAsync(session.SessionId);

        Assert.Equal(SessionState.RecoveryRequired, ambiguous.State);
        Assert.Equal(PrintJobState.Ambiguous, duplicate.PrintJob?.State);
        Assert.Equal(1, booth.Printer.CallCount);

        var resolved = await booth.Workflow.ResolvePrintAsync(
            session.SessionId,
            PrintResolution.ConfirmedPrinted);

        Assert.Equal(SessionState.Completed, resolved.State);
        Assert.Equal(1, booth.Printer.CallCount);
    }

    [Fact]
    public async Task CaptureFailureCanBeCancelledAndResetWithoutOccupyingTheBooth()
    {
        await using var booth = await TestBooth.CreateAsync();
        var session = await booth.StartAsync();
        await booth.AdvanceToCountdownAsync(session.SessionId);
        using var failingWorkflow = booth.CreateWorkflow(new FailingCameraAdapter());

        await Assert.ThrowsAsync<IOException>(() => failingWorkflow.CaptureAsync(session.SessionId));
        var recovery = await failingWorkflow.GetAsync(session.SessionId);
        Assert.Equal(SessionState.RecoveryRequired, recovery.State);

        var cancelled = await failingWorkflow.CancelRecoveryAsync(
            session.SessionId,
            "Camera disconnected before the original completed.");
        await failingWorkflow.ResetAsync(session.SessionId);
        var replacement = await booth.StartAsync();

        Assert.Equal(SessionState.Cancelled, cancelled.State);
        Assert.True(replacement.IsActive);
    }

    [Fact]
    public async Task SimulatedPrinterSerializesConcurrentSubmissions()
    {
        using var printer = new SimulatedPrinterAdapter();
        var submissions = Enumerable.Range(0, 8)
            .Select(index => printer.SubmitAsync(
                new PrintSubmission(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"test:{index}",
                    $"output/{index}.png",
                    Copies: 1),
                CancellationToken.None));

        await Task.WhenAll(submissions);

        Assert.Equal(8, printer.CallCount);
        Assert.Equal(1, printer.MaximumConcurrency);
    }
}

internal sealed class TestBooth : IAsyncDisposable
{
    private const string AssetPath = "assets/logo.png";
    private readonly string _root;
    private readonly TestContextFactory _contextFactory;
    private readonly BoothFileStore _fileStore;
    private readonly SimulatedCameraAdapter _camera;
    private readonly TestBundleSigner _signer;
    private readonly BoothIdentityOptions _identity;
    private readonly FixtureIds _ids;
    private readonly long _priceMinor;

    private TestBooth(
        string root,
        TestContextFactory contextFactory,
        BoothFileStore fileStore,
        SimulatedCameraAdapter camera,
        SimulatedPrinterAdapter printer,
        TestBundleSigner signer,
        BoothIdentityOptions identity,
        FixtureIds ids,
        long priceMinor,
        VerifiedEventBundleStore bundleStore,
        BoothWorkflow workflow)
    {
        _root = root;
        _contextFactory = contextFactory;
        _fileStore = fileStore;
        _camera = camera;
        _signer = signer;
        _identity = identity;
        _ids = ids;
        _priceMinor = priceMinor;
        Printer = printer;
        BundleStore = bundleStore;
        Workflow = workflow;
    }

    public SimulatedPrinterAdapter Printer { get; }

    public VerifiedEventBundleStore BundleStore { get; }

    public BoothWorkflow Workflow { get; }

    public Guid TenantId => _identity.TenantId;

    public Guid DeviceId => _identity.DeviceId;

    public Guid EventId => _ids.EventId;

    public Guid PackageVersionId => _ids.PackageVersionId;

    public Guid NoticeId => _ids.NoticeId;

    public PinnedBundleKey SigningKey => _signer.PublicKey;

    public static async Task<TestBooth> CreateAsync(
        long priceMinor = 0,
        byte[]? assetContent = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "digiphoto-booth-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var contextOptions = new DbContextOptionsBuilder<BoothDbContext>()
            .UseSqlite($"Data Source={Path.Combine(root, "booth.db")}")
            .Options;
        var contextFactory = new TestContextFactory(contextOptions);
        await BoothDatabaseInitializer.InitializeAsync(contextFactory);

        var ids = new FixtureIds(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid());
        var identity = new BoothIdentityOptions(ids.TenantId, ids.DeviceId);
        var signer = new TestBundleSigner();
        var bundleStore = new VerifiedEventBundleStore(
            contextFactory,
            new BoothBundleOptions(Path.Combine(root, "bundles"), signer.PublicKey),
            identity,
            TimeProvider.System);
        var initialBundle = CreateBundle(signer, ids, sequence: 1, priceMinor, ids.TenantId, assetContent);
        if (assetContent is not null)
        {
            await StageAssetsAsync(bundleStore, initialBundle, assetContent);
        }

        await bundleStore.LoadAsync(initialBundle);

        var fileStore = new BoothFileStore(new BoothStorageOptions(Path.Combine(root, "media")));
        var camera = new SimulatedCameraAdapter(fileStore);
        var printer = new SimulatedPrinterAdapter();
        var workflow = new BoothWorkflow(
            contextFactory,
            camera,
            printer,
            fileStore,
            bundleStore,
            identity,
            TimeProvider.System);

        return new TestBooth(
            root,
            contextFactory,
            fileStore,
            camera,
            printer,
            signer,
            identity,
            ids,
            priceMinor,
            bundleStore,
            workflow);
    }

    public BoothWorkflow CreateWorkflow(ICameraAdapter? camera = null) =>
        new(
            _contextFactory,
            camera ?? _camera,
            Printer,
            _fileStore,
            BundleStore,
            _identity,
            TimeProvider.System);

    public SignedEventBundle CreateBundle(
        long sequence,
        Guid? tenantId = null,
        byte[]? assetContent = null) =>
        CreateBundle(
            _signer,
            _ids,
            sequence,
            _priceMinor,
            tenantId ?? TenantId,
            assetContent);

    public Task StageAssetsAsync(SignedEventBundle bundle, byte[] content) =>
        StageAssetsAsync(BundleStore, bundle, content);

    public Task<BoothSessionSnapshot> StartAsync() => Workflow.StartAsync(new StartSessionRequest(
        Guid.NewGuid(),
        EventId));

    public async Task AdvanceToCountdownAsync(Guid sessionId)
    {
        await Workflow.BeginPackageSelectionAsync(sessionId);
        await Workflow.SelectPackageAsync(sessionId, PackageVersionId);
        await Workflow.AcceptPrivacyAsync(sessionId, AcceptedPrivacy());
        await Workflow.StartCountdownAsync(sessionId);
    }

    public async Task AdvanceToRenderingAsync(Guid sessionId)
    {
        await AdvanceToCountdownAsync(sessionId);
        await Workflow.CaptureAsync(sessionId);
        await Workflow.AcceptReviewAsync(sessionId);
    }

    public async Task AdvanceToPrintPendingAsync(Guid sessionId)
    {
        await AdvanceToRenderingAsync(sessionId);
        await Workflow.PersistRenderAsync(
            sessionId,
            new MemoryStream(CreatePng(1200, 1800), writable: false));
    }

    public async Task SeedInflightPrintAsync(Guid sessionId)
    {
        await using var database = _contextFactory.CreateDbContext();
        var session = await database.Sessions.SingleAsync(row => row.Id == sessionId);
        session.State = SessionState.Printing;
        session.UpdatedAtUtc = DateTimeOffset.UtcNow;
        database.PrintJobs.Add(new PrintJobRow
        {
            Id = Guid.NewGuid(),
            TenantId = session.TenantId,
            SessionId = sessionId,
            State = PrintJobState.Pending,
            RequestedCopies = 1,
            IdempotencyKey = $"session:{sessionId:N}:initial-print",
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();
    }

    public async Task SeedCapturedFileBeforeMetadataCommitAsync(Guid sessionId)
    {
        var request = _camera.PlanCapture(TenantId, EventId, sessionId, Guid.NewGuid());
        await using (var database = _contextFactory.CreateDbContext())
        {
            var session = await database.Sessions.SingleAsync(row => row.Id == sessionId);
            session.State = SessionState.Capturing;
            session.UpdatedAtUtc = DateTimeOffset.UtcNow;
            database.Media.Add(new SessionMediaRow
            {
                Id = request.MediaId,
                TenantId = session.TenantId,
                SessionId = sessionId,
                Kind = MediaKind.OriginalPhoto,
                RelativePath = request.RelativePath,
                Sha256 = string.Empty,
                ByteLength = 0,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            });
            await database.SaveChangesAsync();
        }

        await _camera.CaptureAsync(request, CancellationToken.None);
    }

    public async Task CorruptOutputAsync(Guid sessionId)
    {
        var session = await Workflow.GetAsync(sessionId);
        var output = session.Media.Single(item => item.Kind == MediaKind.PrintComposite);
        await _fileStore.WriteBytesAsync(
            output.RelativePath,
            new byte[] { 1, 2, 3, 4 },
            CancellationToken.None);
    }

    public async Task RelabelPersistedBundleAsync(Guid eventId)
    {
        await using var database = _contextFactory.CreateDbContext();
        var row = await database.EventBundles.SingleAsync();
        row.EventId = eventId;
        await database.SaveChangesAsync();
    }

    public async Task<(int MediaCount, int PrintJobCount, int ForeignTenantRecordCount)>
        CountTenantOwnedRecordsAsync(Guid sessionId)
    {
        await using var database = _contextFactory.CreateDbContext();
        var mediaCount = await database.Media.CountAsync(row =>
            row.TenantId == TenantId && row.SessionId == sessionId);
        var printJobCount = await database.PrintJobs.CountAsync(row =>
            row.TenantId == TenantId && row.SessionId == sessionId);
        var foreignTenantRecordCount =
            await database.Media.CountAsync(row => row.TenantId != TenantId) +
            await database.PrintJobs.CountAsync(row => row.TenantId != TenantId);
        return (mediaCount, printJobCount, foreignTenantRecordCount);
    }

    public async Task InsertMismatchedTenantMediaAsync(Guid sessionId)
    {
        await using var database = _contextFactory.CreateDbContext();
        database.Media.Add(new SessionMediaRow
        {
            Id = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            SessionId = sessionId,
            Kind = MediaKind.OriginalPhoto,
            RelativePath = "invalid/cross-tenant.ppm",
            Sha256 = new string('0', 64),
            ByteLength = 1,
            CreatedAtUtc = DateTimeOffset.UtcNow,
        });
        await database.SaveChangesAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Workflow.Dispose();
        Printer.Dispose();
        BundleStore.Dispose();
        _signer.Dispose();
        await Task.Yield();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    public static AcceptPrivacyRequest AcceptedPrivacy()
    {
        var displayed = DateTimeOffset.UtcNow;
        return new AcceptPrivacyRequest(
            DisplayedAtUtc: displayed,
            AssentedAtUtc: displayed.AddSeconds(1),
            AssentingAction: "continue",
            ParticipantsConfirmed: true,
            IncludesMinor: false,
            GuardianConfirmed: false,
            PromotionConsent: false,
            PublicDisplayConsent: false);
    }

    public static byte[] CreatePng(int width, int height)
    {
        using var compressed = new MemoryStream();
        using (var encoder = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var scanline = new byte[checked((width * 4) + 1)];
            for (var row = 0; row < height; row++)
            {
                encoder.Write(scanline);
            }
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), checked((uint)height));
        ihdr[8] = 8;
        ihdr[9] = 6;
        WritePngChunk(png, "IHDR"u8, ihdr);
        WritePngChunk(png, "IDAT"u8, compressed.ToArray());
        WritePngChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static void WritePngChunk(
        Stream destination,
        ReadOnlySpan<byte> chunkType,
        ReadOnlySpan<byte> data)
    {
        Span<byte> integer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(integer, checked((uint)data.Length));
        destination.Write(integer);
        destination.Write(chunkType);
        destination.Write(data);

        var crcInput = new byte[chunkType.Length + data.Length];
        chunkType.CopyTo(crcInput);
        data.CopyTo(crcInput.AsSpan(chunkType.Length));
        BinaryPrimitives.WriteUInt32BigEndian(integer, ComputePngCrc32(crcInput));
        destination.Write(integer);
    }

    private static uint ComputePngCrc32(ReadOnlySpan<byte> value)
    {
        var crc = uint.MaxValue;
        foreach (var item in value)
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) == 0 ? crc >> 1 : 0xedb88320u ^ (crc >> 1);
            }
        }

        return ~crc;
    }

    private static SignedEventBundle CreateBundle(
        TestBundleSigner signer,
        FixtureIds ids,
        long sequence,
        long priceMinor,
        Guid tenantId,
        byte[]? assetContent)
    {
        using var canvas = JsonDocument.Parse("{\"version\":\"7.4.0\",\"objects\":[]}");
        var assetIds = assetContent is null ? Array.Empty<Guid>() : new[] { ids.AssetId };
        var document = new TemplateDocument(
            ContractVersions.TemplateDocument,
            new FabricEngine("fabric", 7),
            new PixelDocument(1200, 1800, 300),
            canvas.RootElement.Clone(),
            assetIds);
        var template = new TemplateVersionSnapshot(
            ids.TemplateId,
            ids.TemplateVersionId,
            "Classic 4x6",
            CanonicalJson.Sha256Hex(document),
            document);
        var assets = assetContent is null
            ? Array.Empty<BundleAsset>()
            : new[]
            {
                new BundleAsset(
                    ids.AssetId,
                    AssetPath,
                    "image/png",
                    assetContent.LongLength,
                    Convert.ToHexStringLower(SHA256.HashData(assetContent))),
            };
        var manifest = new EventBundleManifest(
            ContractVersions.EventBundle,
            Guid.NewGuid(),
            sequence,
            tenantId,
            ids.EventId,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1),
            PaymentEnabled: priceMinor > 0,
            RetentionDays: 30,
            new ThemeSnapshot("Simulator event", "#ffffff", "#111111", null, null),
            new PrivacyNoticeSnapshot(
                ids.NoticeId,
                Version: 1,
                Locale: "en-PH",
                ContentSha256: new string('a', 64),
                ControllerName: "DigiPhoto Test Tenant",
                PrivacyContact: "privacy@example.test",
                AdultContent: "Synthetic test notice.",
                ChildContent: "Synthetic child test notice."),
            [new PackageSnapshot(
                ids.PackageId,
                ids.PackageVersionId,
                "Free 4x6 photo",
                MediaMode.Photo,
                new Money(priceMinor, "PHP"),
                RequiredShots: 1,
                PrintCopies: 1,
                RetakeLimitPerShot: 1,
                CountdownSeconds: 3,
                PrintLayout.FourBySix,
                ids.TemplateVersionId,
                [GuestFilter.Original, GuestFilter.BlackAndWhite])],
            [template],
            assets);
        return signer.Sign(manifest);
    }

    private static async Task StageAssetsAsync(
        VerifiedEventBundleStore store,
        SignedEventBundle bundle,
        byte[] content)
    {
        var asset = Assert.Single(bundle.Manifest.Assets);
        var path = store.GetStagedAssetPath(bundle.Manifest, asset.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content);
    }
}

internal sealed class TestBundleSigner : IDisposable
{
    private readonly DevelopmentBundleSigner _signer = new();

    public TestBundleSigner()
    {
        var publicKey = _signer.DescribePublicKey();
        PublicKey = new PinnedBundleKey(
            publicKey.Algorithm,
            publicKey.KeyId,
            publicKey.SubjectPublicKeyInfoBase64);
    }

    public PinnedBundleKey PublicKey { get; }

    public SignedEventBundle Sign(EventBundleManifest manifest) => _signer.Sign(manifest);

    public void Dispose() => _signer.Dispose();
}

internal sealed class FailingCameraAdapter : ICameraAdapter
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

    public Task<CameraCapture> CaptureAsync(
        CameraCaptureRequest request,
        CancellationToken cancellationToken)
    {
        _ = request;
        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException("Synthetic camera failure.");
    }
}

internal sealed record FixtureIds(
    Guid TenantId,
    Guid DeviceId,
    Guid EventId,
    Guid PackageId,
    Guid PackageVersionId,
    Guid TemplateId,
    Guid TemplateVersionId)
{
    public Guid NoticeId { get; } = Guid.NewGuid();

    public Guid AssetId { get; } = Guid.NewGuid();
}

internal sealed class TestContextFactory(DbContextOptions<BoothDbContext> options)
    : IDbContextFactory<BoothDbContext>
{
    public BoothDbContext CreateDbContext() => new(options);
}
