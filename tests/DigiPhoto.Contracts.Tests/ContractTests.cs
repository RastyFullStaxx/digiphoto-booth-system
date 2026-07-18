using System.Text.Json;
using DigiPhoto.Contracts.Sessions;
using DigiPhoto.Contracts.Templates;

namespace DigiPhoto.Contracts.Tests;

public sealed class ContractTests
{
    [Fact]
    public void MoneyNormalizesCurrencyWithoutUsingFloatingPoint()
    {
        var money = new Money(12_500, "php");

        Assert.Equal(12_500, money.MinorUnits);
        Assert.Equal("PHP", money.Currency);
    }

    [Fact]
    public void ValueContractsRoundTripThroughJson()
    {
        var source = new
        {
            Price = new Money(12_500, "php"),
            Document = new PixelDocument(1200, 1800, 300),
        };

        var json = JsonSerializer.Serialize(source);
        var result = JsonSerializer.Deserialize<ValueContractFixture>(json);

        Assert.NotNull(result);
        Assert.Equal(source.Price, result.Price);
        Assert.Equal(source.Document, result.Document);
    }

    [Theory]
    [InlineData("")]
    [InlineData("PH")]
    [InlineData("PESO")]
    [InlineData("P1P")]
    public void MoneyRejectsInvalidCurrencyCodes(string currency)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Money(100, currency));
    }

    [Fact]
    public void PairedTwoBySixUsesOneStripDuplicatedOnAFourBySixSheet()
    {
        var profile = PrintProfiles.Get(PrintLayout.DuplicatedTwoBySix);

        Assert.Equal(600, profile.LogicalTemplateWidthPx);
        Assert.Equal(1800, profile.LogicalTemplateHeightPx);
        Assert.Equal(1200, profile.SheetWidthPx);
        Assert.Equal(1800, profile.SheetHeightPx);
        Assert.Equal(2, profile.CopiesPerSheet);
    }

    [Fact]
    public void ContractEnumsSerializeAsStableStrings()
    {
        var json = JsonSerializer.Serialize(SessionState.RecoveryRequired);

        Assert.Equal("\"RecoveryRequired\"", json);
    }

    private sealed record ValueContractFixture(Money Price, PixelDocument Document);
}
