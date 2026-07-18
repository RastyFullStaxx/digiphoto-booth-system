using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DigiPhoto.Booth.Storage;

public sealed record BoothStorageOptions(string RootPath);

public sealed record StoredFile(string RelativePath, string Sha256, long ByteLength);

public sealed class BoothFileStore(BoothStorageOptions options)
{
    public const long MaximumRenderBytes = 20 * 1024 * 1024;
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly uint[] Crc32Table = CreateCrc32Table();

    public Task<StoredFile> WriteBytesAsync(
        string relativePath,
        ReadOnlyMemory<byte> bytes,
        CancellationToken cancellationToken) =>
        WriteAsync(
            relativePath,
            new MemoryStream(bytes.ToArray(), writable: false),
            bytes.Length,
            expectedPng: null,
            cancellationToken);

    public Task<StoredFile> WriteRenderPngAsync(
        string relativePath,
        Stream content,
        int expectedWidthPx,
        int expectedHeightPx,
        CancellationToken cancellationToken) =>
        WriteAsync(
            relativePath,
            content,
            MaximumRenderBytes,
            new PngDimensions(expectedWidthPx, expectedHeightPx),
            cancellationToken);

    public async Task<StoredFile?> InspectAsync(
        string relativePath,
        CancellationToken cancellationToken)
    {
        var (normalizedPath, fullPath) = Resolve(relativePath);
        if (!File.Exists(fullPath))
        {
            return null;
        }

        var file = new FileInfo(fullPath);
        var hash = await ComputeSha256Async(fullPath, cancellationToken);
        return new StoredFile(normalizedPath, hash, file.Length);
    }

    private async Task<StoredFile> WriteAsync(
        string relativePath,
        Stream content,
        long maximumBytes,
        PngDimensions? expectedPng,
        CancellationToken cancellationToken)
    {
        var (normalizedPath, fullPath) = Resolve(relativePath);

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

            if (expectedPng is not null)
            {
                await ValidatePngAsync(temporaryPath, expectedPng.Value, cancellationToken);
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

    private static async Task ValidatePngAsync(
        string path,
        PngDimensions expected,
        CancellationToken cancellationToken)
    {
        var png = await File.ReadAllBytesAsync(path, cancellationToken);
        if (expected.WidthPx <= 0 || expected.HeightPx <= 0 || png.Length < 57 ||
            !png.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            throw new ArgumentException("Rendered output is not a structurally valid PNG stream.");
        }

        var offset = PngSignature.Length;
        var chunkIndex = 0;
        var sawIhdr = false;
        var sawIdat = false;
        var idatEnded = false;
        long idatBytes = 0;

        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
            {
                throw new ArgumentException("Rendered PNG ends inside a chunk.");
            }

            var dataLengthValue = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            if (dataLengthValue > int.MaxValue)
            {
                throw new ArgumentException("Rendered PNG contains an oversized chunk.");
            }

            var dataLength = (int)dataLengthValue;
            var typeOffset = offset + 4;
            var dataOffset = typeOffset + 4;
            var crcOffsetValue = (long)dataOffset + dataLength;
            if (crcOffsetValue + 4 > png.LongLength)
            {
                throw new ArgumentException("Rendered PNG chunk length exceeds the file.");
            }

            var crcOffset = (int)crcOffsetValue;
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(crcOffset, 4));
            var actualCrc = ComputeCrc32(png.AsSpan(typeOffset, 4 + dataLength));
            if (expectedCrc != actualCrc)
            {
                throw new ArgumentException("Rendered PNG contains a chunk with an invalid CRC.");
            }

            var chunkType = png.AsSpan(typeOffset, 4);
            if (chunkIndex == 0 && !chunkType.SequenceEqual("IHDR"u8))
            {
                throw new ArgumentException("Rendered PNG must begin with IHDR.");
            }

            if (chunkType.SequenceEqual("IHDR"u8))
            {
                if (sawIhdr || dataLength != 13)
                {
                    throw new ArgumentException("Rendered PNG must contain one 13-byte IHDR chunk.");
                }

                sawIhdr = true;
                ValidateIhdr(png.AsSpan(dataOffset, dataLength), expected);
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (!sawIhdr || idatEnded)
                {
                    throw new ArgumentException("Rendered PNG has invalid IDAT ordering.");
                }

                sawIdat = true;
                idatBytes += dataLength;
            }
            else if (chunkType.SequenceEqual("IEND"u8))
            {
                if (!sawIhdr || !sawIdat || idatBytes == 0 || dataLength != 0 || crcOffset + 4 != png.Length)
                {
                    throw new ArgumentException("Rendered PNG has an invalid or premature IEND chunk.");
                }

                return;
            }
            else if (sawIdat)
            {
                idatEnded = true;
            }

            offset = crcOffset + 4;
            chunkIndex++;
        }

        throw new ArgumentException("Rendered PNG is missing its IEND chunk.");
    }

    private static void ValidateIhdr(ReadOnlySpan<byte> data, PngDimensions expected)
    {
        var width = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));
        if (width != expected.WidthPx || height != expected.HeightPx)
        {
            throw new ArgumentException(
                $"Rendered PNG is {width}x{height}; expected {expected.WidthPx}x{expected.HeightPx}.");
        }

        var bitDepth = data[8];
        var colorType = data[9];
        if (!IsValidBitDepth(bitDepth, colorType) || data[10] != 0 || data[11] != 0 || data[12] > 1)
        {
            throw new ArgumentException("Rendered PNG uses an invalid IHDR encoding profile.");
        }
    }

    private static bool IsValidBitDepth(byte bitDepth, byte colorType) => colorType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        4 => bitDepth is 8 or 16,
        6 => bitDepth is 8 or 16,
        _ => false,
    };

    private static uint ComputeCrc32(ReadOnlySpan<byte> value)
    {
        var crc = uint.MaxValue;
        foreach (var item in value)
        {
            crc = Crc32Table[(crc ^ item) & 0xff] ^ (crc >> 8);
        }

        return ~crc;
    }

    private static uint[] CreateCrc32Table()
    {
        var table = new uint[256];
        for (uint index = 0; index < table.Length; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 0 ? value >> 1 : 0xedb88320u ^ (value >> 1);
            }

            table[index] = value;
        }

        return table;
    }

    private (string NormalizedPath, string FullPath) Resolve(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/').TrimStart('/');
        var fullPath = Path.GetFullPath(normalizedPath, options.RootPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.RootPath)) +
            Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The media path must stay within booth storage.", nameof(relativePath));
        }

        return (normalizedPath, fullPath);
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

    private readonly record struct PngDimensions(int WidthPx, int HeightPx);
}
