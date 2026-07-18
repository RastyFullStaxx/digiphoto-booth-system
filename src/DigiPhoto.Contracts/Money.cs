using System.Globalization;
using System.Text.Json.Serialization;

namespace DigiPhoto.Contracts;

public readonly record struct Money
{
    [JsonConstructor]
    public Money(long minorUnits, string currency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);

        var normalizedCurrency = currency.Trim().ToUpperInvariant();
        if (normalizedCurrency.Length != 3 ||
            normalizedCurrency.Any(character => character is < 'A' or > 'Z'))
        {
            throw new ArgumentException("Currency must be a three-letter ISO code.", nameof(currency));
        }

        MinorUnits = minorUnits;
        Currency = normalizedCurrency;
    }

    public long MinorUnits { get; }

    public string Currency { get; }

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Currency} {MinorUnits}");
}
