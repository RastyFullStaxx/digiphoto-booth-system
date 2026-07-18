using System.Security.Cryptography;

namespace DigiPhoto.Booth.Storage;

public sealed record BoothStorageOptions(string RootPath);

public sealed record StoredFile(string RelativePath, string Sha256, long ByteLength);

public sealed class BoothFileStore(BoothStorageOptions options)
{
    public const long MaximumRenderBytes = 20 * 1024 * 1024;

    public Task<StoredFile> WriteBytesAsync(
        string relativePath,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken) =>
        WriteAsync(relativePath, new MemoryStream(bytes.ToArray(), writable: false), bytes.Length, cancellationToken);

    public Task<StoredFile> WriteRenderAsync(
        string relativePath,
        Stream content,
        CancellationToken cancellationToken) =>
        WriteAsync(relativePath, content, MaximumRenderBytes, cancellationToken);

    private async Task<StoredFile> WriteAsync(
        string relativePath,
        Stream content,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(normalizedPath, options.RootPath);
        var root = Path.GetFullPath(options.RootPath) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The media path must stay within booth storage.", nameof(relativePath));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        long length = 0;

        try
        {
            await using (var destination = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > maximumBytes)
                    {
                        throw new ArgumentException($"Media exceeds the {maximumBytes}-byte limit.");
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                await destination.FlushAsync(cancellationToken);
            }

            if (length == 0)
            {
                throw new ArgumentException("Media cannot be empty.");
            }

            var hash = await ComputeSha256Async(temporaryPath, cancellationToken);
            File.Move(temporaryPath, fullPath, overwrite: true);
            return new StoredFile(normalizedPath, hash, length);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexStringLower(digest);
    }
}
