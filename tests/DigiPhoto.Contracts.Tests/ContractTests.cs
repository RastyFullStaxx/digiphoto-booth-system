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
}
