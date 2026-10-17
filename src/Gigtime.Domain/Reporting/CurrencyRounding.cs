namespace Gigtime.Domain.Reporting;

public static class CurrencyRounding
{
	// #4: ISO 4217 minor-unit exceptions; all other supported currencies default to two decimals.
	public static int Digits(string currency) => currency switch
	{
		"BIF" or "CLP" or "DJF" or "GNF" or "ISK" or "JPY" or "KMF" or "KRW" or "PYG" or "RWF" or "UGX"
			or "UYI" or "VND" or "VUV" or "XAF" or "XOF" or "XPF" => 0,
		"BHD" or "IQD" or "JOD" or "KWD" or "LYD" or "OMR" or "TND" => 3,
		"CLF" or "UYW" => 4,
		_ => 2,
	};

	public static decimal Round(decimal amount, string currency) => decimal.Round(amount, Digits(currency), MidpointRounding.AwayFromZero);
}
