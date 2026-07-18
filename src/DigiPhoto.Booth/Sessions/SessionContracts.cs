using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Sessions;

namespace DigiPhoto.Booth.Sessions;

public sealed record StartSessionRequest(
    Guid SessionId,
    Guid TenantId,
    Guid DeviceId,
    Guid EventId,
    long EventBundleSequence,
    int RetentionDays);

public sealed record SelectPackageRequest(PackageSnapshot Package);

public enum PrintResolution
{
    ConfirmedPrinted,
    CancelWithoutReprint,
}

public sealed record ResolvePrintRequest(PrintResolution Resolution);

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
