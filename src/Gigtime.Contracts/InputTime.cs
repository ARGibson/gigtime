using System.Globalization;
using System.Text.RegularExpressions;

namespace Gigtime.Contracts;

public static partial class InputTime
{
	public static bool IsValid(string text)
	{
		return TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)
			|| Local(text, out _) || (Offset().IsMatch(text)
				&& DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
	}

	public static bool TryResolve(string text, string timezone, DateTimeOffset now, out DateTimeOffset instant)
	{
		instant = default;

		if (Offset().IsMatch(text)) return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out instant);

		var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
		DateTime local;

		if (TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
		{
			var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
			local = date.ToDateTime(time);
		}
		else if (!Local(text, out local))
		{
			return false;
		}

		if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local)) return false;

		instant = new(TimeZoneInfo.ConvertTimeToUtc(local, zone));

		return true;
	}

	private static bool Local(string text, out DateTime time)
	{
		return DateTime.TryParseExact(text,
			["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"],
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out time);
	}

	[GeneratedRegex(@"(?:[zZ]|[+-]\d{2}:\d{2})$")]
	private static partial Regex Offset();
}
