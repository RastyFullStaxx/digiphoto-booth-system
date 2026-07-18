using System.Text.Json.Serialization;
using DigiPhoto.Contracts.Templates;

namespace DigiPhoto.Contracts.Events;

[JsonConverter(typeof(JsonStringEnumConverter<MediaMode>))]
public enum MediaMode
{
    Photo,
    Motion,
}

[JsonConverter(typeof(JsonStringEnumConverter<GuestFilter>))]
public enum GuestFilter
{
    Original,
    BlackAndWhite,
}

public sealed record PackageSnapshot(
    Guid PackageId,
    Guid VersionId,
    string Name,
    MediaMode MediaMode,
    Money Price,
    int RequiredShots,
    int PrintCopies,
    int RetakeLimitPerShot,
    int CountdownSeconds,
    PrintLayout PrintLayout,
    Guid TemplateVersionId,
    IReadOnlyList<GuestFilter> GuestFilters);

public sealed record PrivacyNoticeSnapshot(
    Guid NoticeId,
    int Version,
    string Locale,
    string ContentSha256,
    string ControllerName,
    string PrivacyContact,
    string AdultContent,
    string ChildContent);

public sealed record ThemeSnapshot(
    string EventName,
    string PrimaryColor,
    string AccentColor,
    Guid? LogoAssetId,
    Guid? AttractMediaAssetId);

public sealed record BundleAsset(
    Guid AssetId,
    string RelativePath,
    string MediaType,
    long ByteLength,
    string Sha256);

public sealed record EventBundleManifest(
    int SchemaVersion,
    Guid BundleId,
    long Sequence,
    Guid TenantId,
    Guid EventId,
    DateTimeOffset IssuedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    bool PaymentEnabled,
    int RetentionDays,
    ThemeSnapshot Theme,
    PrivacyNoticeSnapshot PrivacyNotice,
    IReadOnlyList<PackageSnapshot> Packages,
    IReadOnlyList<TemplateVersionSnapshot> Templates,
    IReadOnlyList<BundleAsset> Assets);

public sealed record BundleSignature(
    string Algorithm,
    string KeyId,
    string ValueBase64Url);

public sealed record SignedEventBundle(
    EventBundleManifest Manifest,
    BundleSignature Signature);
