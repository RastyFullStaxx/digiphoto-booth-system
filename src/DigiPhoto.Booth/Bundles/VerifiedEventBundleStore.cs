using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigiPhoto.Booth.Configuration;
using DigiPhoto.Booth.Data;
using DigiPhoto.Contracts;
using DigiPhoto.Contracts.Events;
using Microsoft.EntityFrameworkCore;

namespace DigiPhoto.Booth.Bundles;

public sealed record PinnedBundleKey(
    string Algorithm,
    string KeyId,
    string SubjectPublicKeyInfoBase64);

public sealed record BoothBundleOptions(string RootPath, PinnedBundleKey PinnedKey);

public sealed record VerifiedBundleSnapshot(
    Guid BundleId,
    Guid TenantId,
    Guid EventId,
    long Sequence,
    string ManifestSha256,
    DateTimeOffset ExpiresAtUtc);

public sealed class EventBundleVerificationException(string message) : InvalidOperationException(message);

public sealed class VerifiedEventBundleStore(
    IDbContextFactory<BoothDbContext> contextFactory,
    BoothBundleOptions options,
    BoothIdentityOptions identity,
    TimeProvider clock) : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    public void Dispose() => _loadGate.Dispose();

    public async Task<VerifiedBundleSnapshot> LoadAsync(
        SignedEventBundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var manifestHash = await VerifyAsync(bundle, requireNotExpired: true, cancellationToken);

        await _loadGate.WaitAsync(cancellationToken);
        try
        {
            await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
            var latest = await database.EventBundles
                .Where(row => row.EventId == bundle.Manifest.EventId)
                .OrderByDescending(row => row.Sequence)
                .FirstOrDefaultAsync(cancellationToken);

            if (latest is not null && bundle.Manifest.Sequence < latest.Sequence)
            {
                throw new EventBundleVerificationException(
                    $"Bundle sequence {bundle.Manifest.Sequence} is older than accepted sequence {latest.Sequence}.");
            }

            if (latest is not null && bundle.Manifest.Sequence == latest.Sequence)
            {
                if (latest.BundleId != bundle.Manifest.BundleId ||
                    !FixedTimeHexEquals(latest.ManifestSha256, manifestHash))
                {
                    throw new EventBundleVerificationException(
                        "A different bundle already occupies this event sequence.");
                }

                EnsureStoredRowMatches(
                    latest,
                    bundle,
                    bundle.Manifest.EventId,
                    bundle.Manifest.Sequence,
                    manifestHash);
                return Map(latest);
            }

            var row = new EventBundleRow
            {
                BundleId = bundle.Manifest.BundleId,
                TenantId = bundle.Manifest.TenantId,
                EventId = bundle.Manifest.EventId,
                Sequence = bundle.Manifest.Sequence,
                SignedBundleJson = JsonSerializer.Serialize(bundle, JsonOptions),
                ManifestSha256 = manifestHash,
                SigningKeyId = bundle.Signature.KeyId,
                IssuedAtUtc = bundle.Manifest.IssuedAtUtc,
                ExpiresAtUtc = bundle.Manifest.ExpiresAtUtc,
                LoadedAtUtc = clock.GetUtcNow(),
            };
            database.EventBundles.Add(row);
            await database.SaveChangesAsync(cancellationToken);
            return Map(row);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    public async Task<SignedEventBundle> GetLatestStartableAsync(
        Guid eventId,
        CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await database.EventBundles
            .AsNoTracking()
            .Where(item => item.EventId == eventId)
            .OrderByDescending(item => item.Sequence)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new EventBundleVerificationException("No verified event bundle is loaded for this event.");
        var bundle = Deserialize(row);
        var manifestHash = await VerifyAsync(bundle, requireNotExpired: true, cancellationToken);
        EnsureStoredRowMatches(row, bundle, eventId, expectedSequence: null, manifestHash);
        return bundle;
    }

    public async Task<SignedEventBundle> GetExactAsync(
        Guid eventId,
        long sequence,
        CancellationToken cancellationToken = default)
    {
        await using var database = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await database.EventBundles
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.EventId == eventId && item.Sequence == sequence,
                cancellationToken)
            ?? throw new EventBundleVerificationException("The session's verified event bundle is unavailable.");
        var bundle = Deserialize(row);
        var manifestHash = await VerifyAsync(bundle, requireNotExpired: false, cancellationToken);
        EnsureStoredRowMatches(row, bundle, eventId, sequence, manifestHash);
        return bundle;
    }

    private async Task<string> VerifyAsync(
        SignedEventBundle bundle,
        bool requireNotExpired,
        CancellationToken cancellationToken)
    {
        var manifest = bundle.Manifest;
        if (manifest.SchemaVersion != ContractVersions.EventBundle ||
            manifest.BundleId == Guid.Empty || manifest.TenantId == Guid.Empty ||
            manifest.EventId == Guid.Empty || manifest.Sequence <= 0)
        {
            throw new EventBundleVerificationException("Bundle identity, sequence, or schema version is invalid.");
        }

        if (identity.TenantId == Guid.Empty || manifest.TenantId != identity.TenantId)
        {
            throw new EventBundleVerificationException(
                "The event bundle tenant does not match this booth's configured tenant.");
        }

        var now = clock.GetUtcNow();
        if (manifest.IssuedAtUtc > now.AddMinutes(5) || manifest.ExpiresAtUtc <= manifest.IssuedAtUtc ||
            (requireNotExpired && manifest.ExpiresAtUtc <= now))
        {
            throw new EventBundleVerificationException("The event bundle is expired or has invalid issue dates.");
        }

        if (manifest.RetentionDays is not (7 or 30 or 90))
        {
            throw new EventBundleVerificationException("Bundle retention must be 7, 30, or 90 days.");
        }

        VerifySignature(bundle);
        VerifyTemplatesAndPackages(manifest);
        await VerifyAssetsAsync(manifest, cancellationToken);
        return BundleCanonicalJson.Sha256Hex(manifest);
    }

    private void VerifySignature(SignedEventBundle bundle)
    {
        var pinnedKey = options.PinnedKey;
        if (pinnedKey.Algorithm != "ES256" || string.IsNullOrWhiteSpace(pinnedKey.KeyId) ||
            string.IsNullOrWhiteSpace(pinnedKey.SubjectPublicKeyInfoBase64))
        {
            throw new EventBundleVerificationException("A pinned ES256 event-bundle key is not configured.");
        }

        if (bundle.Signature.Algorithm != pinnedKey.Algorithm ||
            bundle.Signature.KeyId != pinnedKey.KeyId)
        {
            throw new EventBundleVerificationException("The event bundle was not signed by the pinned key.");
        }

        try
        {
            var signature = Base64UrlDecode(bundle.Signature.ValueBase64Url);
            if (signature.Length != 64)
            {
                throw new EventBundleVerificationException("The ES256 signature has an invalid length.");
            }

            using var verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(pinnedKey.SubjectPublicKeyInfoBase64),
                out var bytesRead);
            if (bytesRead == 0 || !verifier.VerifyData(
                    BundleCanonicalJson.Serialize(bundle.Manifest),
                    signature,
                    HashAlgorithmName.SHA256,
                    DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
            {
                throw new EventBundleVerificationException("The event bundle signature is invalid.");
            }
        }
        catch (EventBundleVerificationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            throw new EventBundleVerificationException(
                $"The pinned key or bundle signature is malformed: {exception.Message}");
        }
    }

    private static void VerifyTemplatesAndPackages(EventBundleManifest manifest)
    {
        var templateIds = new HashSet<Guid>();
        foreach (var template in manifest.Templates)
        {
            if (template.VersionId == Guid.Empty || !templateIds.Add(template.VersionId) ||
                template.Document.SchemaVersion != ContractVersions.TemplateDocument ||
                !FixedTimeHexEquals(
                    template.ContentSha256,
                    BundleCanonicalJson.Sha256Hex(template.Document)))
            {
                throw new EventBundleVerificationException(
                    $"Template version {template.VersionId} failed identity, schema, or content-hash validation.");
            }
        }

        var packageIds = new HashSet<Guid>();
        foreach (var package in manifest.Packages)
        {
            if (package.PackageId == Guid.Empty || package.VersionId == Guid.Empty ||
                !packageIds.Add(package.VersionId) || !templateIds.Contains(package.TemplateVersionId))
            {
                throw new EventBundleVerificationException(
                    $"Package version {package.VersionId} is duplicated or references an unknown template.");
            }
        }
    }

    private async Task VerifyAssetsAsync(
        EventBundleManifest manifest,
        CancellationToken cancellationToken)
    {
        var assetIds = new HashSet<Guid>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            if (asset.AssetId == Guid.Empty || !assetIds.Add(asset.AssetId) ||
                string.IsNullOrWhiteSpace(asset.RelativePath) || Path.IsPathRooted(asset.RelativePath))
            {
                throw new EventBundleVerificationException("A bundle asset has an invalid ID or path.");
            }

            var normalizedPath = asset.RelativePath.Replace('\\', '/').TrimStart('/');
            if (!paths.Add(normalizedPath))
            {
                throw new EventBundleVerificationException($"Bundle asset path {normalizedPath} is duplicated.");
            }

            var fullPath = GetStagedAssetPath(manifest, normalizedPath);
            if (!File.Exists(fullPath))
            {
                throw new EventBundleVerificationException($"Bundle asset {normalizedPath} is missing.");
            }

            var file = new FileInfo(fullPath);
            if (file.Length != asset.ByteLength)
            {
                throw new EventBundleVerificationException($"Bundle asset {normalizedPath} has the wrong length.");
            }

            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var actualHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!FixedTimeHexEquals(asset.Sha256, actualHash))
            {
                throw new EventBundleVerificationException($"Bundle asset {normalizedPath} failed its hash check.");
            }
        }
    }

    public string GetStagedAssetPath(EventBundleManifest manifest, string relativePath)
    {
        var stagingRoot = Path.Combine(
            options.RootPath,
            manifest.TenantId.ToString("N"),
            manifest.EventId.ToString("N"),
            manifest.BundleId.ToString("N"));
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(stagingRoot)) +
            Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(relativePath, stagingRoot);
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new EventBundleVerificationException("A bundle asset path escapes the bundle root.");
        }

        return fullPath;
    }

    private static SignedEventBundle Deserialize(EventBundleRow row) =>
        JsonSerializer.Deserialize<SignedEventBundle>(row.SignedBundleJson, JsonOptions)
        ?? throw new EventBundleVerificationException("The stored event bundle cannot be read.");

    private static void EnsureStoredRowMatches(
        EventBundleRow row,
        SignedEventBundle bundle,
        Guid expectedEventId,
        long? expectedSequence,
        string manifestHash)
    {
        var manifest = bundle.Manifest;
        if (manifest.EventId != expectedEventId ||
            (expectedSequence.HasValue && manifest.Sequence != expectedSequence.Value) ||
            row.BundleId != manifest.BundleId ||
            row.TenantId != manifest.TenantId ||
            row.EventId != manifest.EventId ||
            row.Sequence != manifest.Sequence ||
            !FixedTimeHexEquals(row.ManifestSha256, manifestHash) ||
            !string.Equals(row.SigningKeyId, bundle.Signature.KeyId, StringComparison.Ordinal) ||
            row.IssuedAtUtc != manifest.IssuedAtUtc ||
            row.ExpiresAtUtc != manifest.ExpiresAtUtc)
        {
            throw new EventBundleVerificationException(
                "The stored event-bundle index does not match its signed manifest.");
        }
    }

    private static VerifiedBundleSnapshot Map(EventBundleRow row) => new(
        row.BundleId,
        row.TenantId,
        row.EventId,
        row.Sequence,
        row.ManifestSha256,
        row.ExpiresAtUtc);

    private static bool FixedTimeHexEquals(string expected, string actual)
    {
        if (expected.Length != 64 || actual.Length != 64 ||
            !expected.All(Uri.IsHexDigit) || !actual.All(Uri.IsHexDigit))
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected.ToLowerInvariant()),
            Encoding.ASCII.GetBytes(actual.ToLowerInvariant()));
    }

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

// This byte contract mirrors the cloud signer. A compatibility test must fail if either side changes.
public static class BundleCanonicalJson
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

    public static string Sha256Hex<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(Serialize(value)));

    private static void WriteElement(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject()
                             .OrderBy(property => property.Name, StringComparer.Ordinal))
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
