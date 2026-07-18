using System.Text.Json.Serialization;
using DigiPhoto.Contracts.Events;

namespace DigiPhoto.Contracts.Sessions;

[JsonConverter(typeof(JsonStringEnumConverter<SessionState>))]
public enum SessionState
{
    Attract,
    PackageSelection,
    PrivacyNotice,
    PaymentPending,
    LivePreview,
    Countdown,
    Capturing,
    Review,
    Rendering,
    PrintPending,
    Printing,
    Completed,
    RecoveryRequired,
    Cancelled,
}

[JsonConverter(typeof(JsonStringEnumConverter<PaymentAttemptState>))]
public enum PaymentAttemptState
{
    NotRequired,
    Pending,
    Succeeded,
    Failed,
    Expired,
    Cancelled,
    Ambiguous,
    OverrideAuthorized,
}

[JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
public enum MediaKind
{
    OriginalPhoto,
    PrintComposite,
    GalleryPhoto,
    Gif,
    Video,
}

[JsonConverter(typeof(JsonStringEnumConverter<PrintJobState>))]
public enum PrintJobState
{
    NotRequested,
    Pending,
    Submitted,
    Completed,
    Ambiguous,
    Failed,
}

public sealed record PrivacyRecord(
    Guid NoticeId,
    int NoticeVersion,
    string NoticeSha256,
    string Locale,
    string LawfulBasis,
    DateTimeOffset DisplayedAtUtc,
    DateTimeOffset AssentedAtUtc,
    string AssentingAction,
    bool ParticipantsConfirmed,
    bool IncludesMinor,
    bool GuardianConfirmed,
    bool PromotionConsent,
    bool PublicDisplayConsent);

public sealed record MediaInventoryItem(
    Guid MediaId,
    MediaKind Kind,
    string RelativePath,
    string Sha256,
    long ByteLength,
    int? WidthPx,
    int? HeightPx,
    DateTimeOffset CreatedAtUtc);

public sealed record PaymentAttemptReference(
    Guid PaymentAttemptId,
    PaymentAttemptState State,
    Money Amount,
    DateTimeOffset? ExpiresAtUtc,
    string? ProviderPaymentId);

public sealed record PrintJobReference(
    Guid PrintJobId,
    PrintJobState State,
    int RequestedCopies,
    string IdempotencyKey,
    DateTimeOffset? SubmittedAtUtc);

public sealed record SessionManifest(
    int SchemaVersion,
    Guid SessionId,
    Guid TenantId,
    Guid DeviceId,
    Guid EventId,
    Guid PackageVersionId,
    Guid TemplateVersionId,
    long EventBundleSequence,
    SessionState State,
    PrivacyRecord Privacy,
    PaymentAttemptReference Payment,
    PrintJobReference PrintJob,
    IReadOnlyList<MediaInventoryItem> Media,
    int RetentionDays,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset? CompletedAtUtc);
