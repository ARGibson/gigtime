using System.Globalization;
using System.Text.RegularExpressions;
using NodaTime;

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

		var zone = TimeZones.Get(timezone);
		DateTime local;

		if (TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
		{
			var date = Instant.FromDateTimeOffset(now).InZone(zone).Date;
			local = date.At(new LocalTime(time.Hour, time.Minute)).ToDateTimeUnspecified();
		}
		else if (!Local(text, out local))
		{
			return false;
		}

		var mapping = zone.MapLocal(LocalDateTime.FromDateTime(local));

		if (mapping.Count != 1) return false;

		instant = mapping.Single().ToInstant().ToDateTimeOffset();

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
