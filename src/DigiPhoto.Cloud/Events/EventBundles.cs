using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiPhoto.Cloud.Data;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using DigiPhoto.Contracts.Templates;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Cloud.Events;

public static class CanonicalJson
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize<T>(T value)
    {
        using var document = JsonSerializer.SerializeToDocument(value, SerializerOptions);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            WriteElement(writer, document.RootElement);
        }

        return stream.ToArray();
    }

    public static string Sha256Hex<T>(T value) => Sha256Hex(Serialize(value));

    public static string Sha256Hex(ReadOnlySpan<byte> value) =>
        Convert.ToHexStringLower(SHA256.HashData(value));

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteElement(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteElement(writer, item);
                }

                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new InvalidOperationException($"Unsupported JSON value kind: {element.ValueKind}.");
        }
    }
}

public sealed record DevelopmentSigningKey(
    string Algorithm,
    string KeyId,
    string SubjectPublicKeyInfoBase64);

public sealed class DevelopmentBundleSigner : IDisposable
{
    private readonly ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly Lock sync = new();

    public DevelopmentBundleSigner()
    {
        var publicKey = signingKey.ExportSubjectPublicKeyInfo();
        KeyId = $"development-{CanonicalJson.Sha256Hex(publicKey)[..16]}";
    }

    public string KeyId { get; }

    public DevelopmentSigningKey DescribePublicKey()
    {
        lock (sync)
        {
            return new DevelopmentSigningKey(
                "ES256",
                KeyId,
                Convert.ToBase64String(signingKey.ExportSubjectPublicKeyInfo()));
        }
    }

    public SignedEventBundle Sign(EventBundleManifest manifest)
    {
        var canonicalManifest = CanonicalJson.Serialize(manifest);
        byte[] signature;
        lock (sync)
        {
            signature = signingKey.SignData(
                canonicalManifest,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }

        return new SignedEventBundle(
            manifest,
            new BundleSignature("ES256", KeyId, Base64UrlEncode(signature)));
    }

    public static bool Verify(
        EventBundleManifest manifest,
        BundleSignature signature,
        DevelopmentSigningKey publicKey)
    {
        if (signature.Algorithm != "ES256" || signature.KeyId != publicKey.KeyId)
        {
            return false;
        }

        using var verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey.SubjectPublicKeyInfoBase64), out _);
        return verifier.VerifyData(
            CanonicalJson.Serialize(manifest),
            Base64UrlDecode(signature.ValueBase64Url),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public void Dispose() => signingKey.Dispose();

    private static string Base64UrlEncode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new FormatException("Invalid base64url value."),
        };
        return Convert.FromBase64String(padded);
    }
}

public sealed class EventBundlePublisher(
    CloudDbContext database,
    DevelopmentBundleSigner signer)
{
    public async Task<SignedEventBundle?> GetAsync(Guid eventId, CancellationToken cancellationToken)
    {
        var cloudEvent = await database.Events
            .AsNoTracking()
            .SingleOrDefaultAsync(row => row.Id == eventId, cancellationToken);
        if (cloudEvent is null)
        {
            return null;
        }

        var packages = await database.Packages
            .AsNoTracking()
            .Where(row => row.EventId == eventId)
            .OrderBy(row => row.VersionId)
            .ToListAsync(cancellationToken);
        var templates = await database.TemplateVersions
            .AsNoTracking()
            .Where(row => row.EventId == eventId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var assets = await database.Assets
            .AsNoTracking()
            .Where(row => row.EventId == eventId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);

        var templateSnapshots = templates.Select(ToSnapshot).ToArray();
        foreach (var template in templateSnapshots)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(template.ContentSha256),
                    Encoding.ASCII.GetBytes(CanonicalJson.Sha256Hex(template.Document))))
            {
                throw new InvalidDataException($"Template version {template.VersionId} failed its content hash check.");
            }
        }

        foreach (var asset in assets)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.ASCII.GetBytes(asset.Sha256),
                    Encoding.ASCII.GetBytes(CanonicalJson.Sha256Hex(asset.Content))))
            {
                throw new InvalidDataException($"Asset {asset.Id} failed its content hash check.");
            }
        }

        var manifest = new EventBundleManifest(
            ContractVersions.EventBundle,
            cloudEvent.BundleId,
            cloudEvent.BundleSequence,
            cloudEvent.TenantId,
            cloudEvent.Id,
            cloudEvent.IssuedAtUtc,
            cloudEvent.ExpiresAtUtc,
            cloudEvent.PaymentEnabled,
            cloudEvent.RetentionDays,
            new ThemeSnapshot(
                cloudEvent.Name,
                cloudEvent.PrimaryColor,
                cloudEvent.AccentColor,
                cloudEvent.LogoAssetId,
                null),
            new PrivacyNoticeSnapshot(
                cloudEvent.NoticeId,
                cloudEvent.NoticeVersion,
                cloudEvent.NoticeLocale,
                cloudEvent.NoticeSha256,
                cloudEvent.ControllerName,
                cloudEvent.PrivacyContact,
                cloudEvent.AdultNotice,
                cloudEvent.ChildNotice),
            packages.Select(ToSnapshot).ToArray(),
            templateSnapshots,
            assets.Select(asset => new BundleAsset(
                asset.Id,
                asset.RelativePath,
                asset.MediaType,
                asset.Content.LongLength,
                asset.Sha256)).ToArray());

        return signer.Sign(manifest);
    }

    private static PackageSnapshot ToSnapshot(PackageRecord package) => new(
        package.Id,
        package.VersionId,
        package.Name,
        package.MediaMode,
        new Money(package.PriceMinor, package.Currency),
        package.RequiredShots,
        package.PrintCopies,
        package.RetakeLimitPerShot,
        package.CountdownSeconds,
        package.PrintLayout,
        package.TemplateVersionId,
        JsonSerializer.Deserialize<GuestFilter[]>(package.GuestFiltersJson)
            ?? throw new InvalidDataException($"Package {package.VersionId} has no guest filters."));

    private static TemplateVersionSnapshot ToSnapshot(TemplateVersionRecord template)
    {
        using var canvas = JsonDocument.Parse(template.CanvasJson);
        return new TemplateVersionSnapshot(
            template.TemplateId,
            template.Id,
            template.Name,
            template.ContentSha256,
            new TemplateDocument(
                template.SchemaVersion,
                new FabricEngine("fabric", template.FabricMajorVersion),
                new PixelDocument(template.WidthPx, template.HeightPx, template.Dpi),
                canvas.RootElement.Clone(),
                JsonSerializer.Deserialize<Guid[]>(template.AssetIdsJson)
                    ?? throw new InvalidDataException($"Template {template.Id} has no asset inventory.")));
    }
}
