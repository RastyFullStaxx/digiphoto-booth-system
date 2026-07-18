using DigiPhoto.Contracts.Sessions;

namespace DigiPhoto.Booth.Sessions;

public sealed record StartSessionRequest(
    Guid SessionId,
    Guid EventId);

public sealed record SelectPackageRequest(Guid PackageVersionId);

public sealed record AcceptPrivacyRequest(
    DateTimeOffset DisplayedAtUtc,
    DateTimeOffset AssentedAtUtc,
    string AssentingAction,
    bool ParticipantsConfirmed,
    bool IncludesMinor,
    bool GuardianConfirmed,
    bool PromotionConsent,
    bool PublicDisplayConsent);

public enum PrintResolution
{
    ConfirmedPrinted,
    CancelWithoutReprint,
}

public sealed record ResolvePrintRequest(PrintResolution Resolution);

public sealed record CancelRecoveryRequest(string Reason);

public sealed record BoothSessionSnapshot(
    int SchemaVersion,
    Guid SessionId,
    Guid TenantId,
    Guid DeviceId,
    Guid EventId,
    Guid? PackageVersionId,
    Guid? TemplateVersionId,
    long EventBundleSequence,
    SessionState State,
    bool IsActive,
    int RequiredShots,
    int CapturedShots,
    int PrintCopies,
    PrivacyRecord? Privacy,
    string? RecoveryReason,
    PaymentAttemptReference? Payment,
    PrintJobReference? PrintJob,
    IReadOnlyList<MediaInventoryItem> Media,
    int RetentionDays,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);

public sealed class BoothWorkflowException : InvalidOperationException
{
    public BoothWorkflowException(string message)
        : base(message)
    {
    }

    public BoothWorkflowException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
