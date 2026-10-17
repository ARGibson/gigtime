using System.Globalization;

namespace Gigtime.Contracts.Reporting;

public static class ReportDates
{
	public static bool TryParse(string? range, out DateOnly? from, out DateOnly? until)
	{
		from = null;
		until = null;

		if (range is null) return true;

		var parts = range.Split("..");

		if (parts.Length != 2
				|| !DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
				|| !DateOnly.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
				|| end < start || end == DateOnly.MaxValue)
		{
			return false;
		}

		from = start;
		until = end.AddDays(1);

		return true;
	}
}
