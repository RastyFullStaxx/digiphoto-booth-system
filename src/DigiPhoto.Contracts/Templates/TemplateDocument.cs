using System.Text.Json;

namespace DigiPhoto.Contracts.Templates;

public sealed record FabricEngine(string Name, int MajorVersion);

public sealed record PixelDocument
{
    public PixelDocument(int widthPx, int heightPx, int dpi)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPx);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dpi);

        WidthPx = widthPx;
        HeightPx = heightPx;
        Dpi = dpi;
    }

    public int WidthPx { get; }

    public int HeightPx { get; }

    public int Dpi { get; }
}

public sealed record TemplateDocument(
    int SchemaVersion,
    FabricEngine Engine,
    PixelDocument Document,
    JsonElement Canvas,
    IReadOnlyList<Guid> AssetIds);

public sealed record TemplateVersionSnapshot(
    Guid TemplateId,
    Guid VersionId,
    string Name,
    string ContentSha256,
    TemplateDocument Document);
