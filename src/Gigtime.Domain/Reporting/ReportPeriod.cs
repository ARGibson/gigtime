using Gigtime.Domain.Catalogue;

namespace Gigtime.Domain.Reporting;

/// <summary>Local calendar dates avoid assuming every day lasts 24 hours across DST.</summary>
public sealed record ReportPeriod(DateOnly From, DateOnly UntilExclusive)
{
	public bool Contains(DateTimeOffset instant, string timezone)
	{
		var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timezone)).DateTime);

		return date >= From && date < UntilExclusive;
	}

	public static ReportPeriod For(ReportQuery query, ResolvedSettings settings)
	{
		ArgumentNullException.ThrowIfNull(query);
		ArgumentNullException.ThrowIfNull(settings);
		var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(query.At, TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone)).DateTime);

		return query.Kind switch
		{
			PeriodKind.Week => Week(local, settings.WeekStarts),
			PeriodKind.Month => new(new(local.Year, local.Month, 1), new DateOnly(local.Year, local.Month, 1).AddMonths(1)),
			PeriodKind.Range when query.From is { } from && query.UntilExclusive is { } until && until > from => new(from, until),
			_ => throw new ArgumentException("Supply an ordered local date range or a supported period.", nameof(query)),
		};
	}

	private static ReportPeriod Week(DateOnly date, DayOfWeek weekStarts)
	{
		var from = date.AddDays(-((7 + (int)date.DayOfWeek - (int)weekStarts) % 7));

		return new(from, from.AddDays(7));
	}
}
