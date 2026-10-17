using System.Collections.Frozen;
using System.Text.RegularExpressions;

namespace Gigtime.Domain.Catalogue;

public static partial class CatalogueRules
{
  // ISO 4217 current currency/fund codes: SIX List One, published 2026-09-17.
  // https://www.six-group.com/dam/download/financial-information/data-center/iso-currrency/lists/list-one.xml
  private static readonly FrozenSet<string> currencies = (
	  "AED AFN ALL AMD AOA ARS AUD AWG AZN BAM BBD BDT BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD " +
	  "CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP " +
	  "GBP GEL GHS GIP GMD GNF GTQ GYD HKD HNL HTG HUF IDR ILS INR IQD IRR ISK JMD JOD JPY KES KGS KHR " +
	  "KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK " +
	  "MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR " +
	  "SBD SCR SDG SEK SGD SHP SLE SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH " +
	  "UGX USD USN UYI UYU UYW UZS VED VES VND VUV WST XAD XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR " +
	  "XOF XPD XPF XPT XSU XTS XUA XXX YER ZAR ZMW ZWG").Split(' ').ToFrozenSet(StringComparer.Ordinal);

  [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,31}\\z", RegexOptions.CultureInvariant)]
  private static partial Regex KeyPattern();
  public static bool ValidKey(string? key) => key is not null && KeyPattern().IsMatch(key);
  public static bool ValidCurrency(string? currency) => currency is not null && currencies.Contains(currency);
  public static bool ValidRounding(string? rounding) => rounding is null or "nearest-minute";
  public static bool ValidTimezone(string? timezone) => timezone is null || TimeZoneInfo.TryFindSystemTimeZoneById(timezone, out _);
  public static bool ValidId(Guid id) => id.Version == 7;
}
