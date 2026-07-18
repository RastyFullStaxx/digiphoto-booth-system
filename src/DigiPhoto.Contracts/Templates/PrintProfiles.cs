using System.Text.Json.Serialization;

namespace DigiPhoto.Contracts.Templates;

[JsonConverter(typeof(JsonStringEnumConverter<PrintLayout>))]
public enum PrintLayout
{
    FourBySix,
    DuplicatedTwoBySix,
}

public sealed record PrintProfileDefinition(
    PrintLayout Layout,
    int SheetWidthPx,
    int SheetHeightPx,
    int Dpi,
    int LogicalTemplateWidthPx,
    int LogicalTemplateHeightPx,
    int CopiesPerSheet);

public static class PrintProfiles
{
    public static readonly PrintProfileDefinition FourBySix = new(
        PrintLayout.FourBySix,
        SheetWidthPx: 1200,
        SheetHeightPx: 1800,
        Dpi: 300,
        LogicalTemplateWidthPx: 1200,
        LogicalTemplateHeightPx: 1800,
        CopiesPerSheet: 1);

    public static readonly PrintProfileDefinition DuplicatedTwoBySix = new(
        PrintLayout.DuplicatedTwoBySix,
        SheetWidthPx: 1200,
        SheetHeightPx: 1800,
        Dpi: 300,
        LogicalTemplateWidthPx: 600,
        LogicalTemplateHeightPx: 1800,
        CopiesPerSheet: 2);

    public static PrintProfileDefinition Get(PrintLayout layout) => layout switch
    {
        PrintLayout.FourBySix => FourBySix,
        PrintLayout.DuplicatedTwoBySix => DuplicatedTwoBySix,
        _ => throw new ArgumentOutOfRangeException(nameof(layout), layout, "Unsupported print layout."),
    };
}
