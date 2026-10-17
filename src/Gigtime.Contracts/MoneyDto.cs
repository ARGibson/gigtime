using System.Globalization;

namespace Gigtime.Contracts;

/// <summary>Decimal strings avoid a caller losing money precision through binary floating point.</summary>
public sealed record MoneyDto(string Amount, string Currency)
{
  public static MoneyDto From(decimal amount, string currency) => new(amount.ToString(CultureInfo.InvariantCulture), currency);
}
