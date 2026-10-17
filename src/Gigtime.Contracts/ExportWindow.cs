using System.Globalization;

namespace Gigtime.Contracts;

public static class ExportWindow
{
	public static bool TryResolve(string? from, string? to, DateTimeOffset now, out DateTimeOffset? start, out DateTimeOffset? end)
	{
		start = null;
		end = null;

		if (!Bound(from, false, now, out start) || !Bound(to, true, now, out end)) return false;

		return start is null || end is null || start <= end;
	}

	private static bool Bound(string? text, bool end, DateTimeOffset now, out DateTimeOffset? value)
	{
		value = null;

		if (text is null) return true;

		if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
		{
			if (date == DateOnly.MaxValue) return false;

			value = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
			if (end) value = value.Value.AddDays(1).AddMilliseconds(-1);

			return true;
		}

		if (!InputTime.TryResolve(text, "UTC", now, out var instant)) return false;

		value = instant;

		return true;
	}
}
