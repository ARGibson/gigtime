using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

public static class TimesheetMath
{
	public static TimesheetResult Calculate(IEnumerable<TaskView> tasks, CatalogueView catalogue, ReportQuery query, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(tasks);
		ArgumentNullException.ThrowIfNull(catalogue);
		ArgumentNullException.ThrowIfNull(query);
		var slices = new List<(DateOnly Date, TaskView Task, long Seconds)>();

		foreach (var task in ReportTasks.Select(tasks, catalogue, query))
		{
			var settings = catalogue.Resolve(task.ProjectId);
			var period = ReportPeriod.For(query, settings);
			var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.Timezone);
			var from = Midnight(period.From, zone);
			var until = Midnight(period.UntilExclusive, zone);

			foreach (var interval in task.Intervals)
			{
				var start = interval.Start > from ? interval.Start : from;
				var end = interval.End ?? TaskMath.Instant(now);
				if (end > until) end = until;

				while (start < end)
				{
					var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
					var next = Midnight(date.AddDays(1), zone);
					// #35: a backwards date shift can put the first midnight before this interval.
					if (next <= start) next = Midnight(date.AddDays(1), zone, start);
					var boundary = next > start && next < end ? next : end;
					var seconds = (boundary - start).Ticks / TimeSpan.TicksPerSecond;
					if (seconds > 0) slices.Add((date, task, seconds));
					start = boundary;
				}
			}
		}

		var days = slices
			.GroupBy(slice => slice.Date)
			.OrderBy(day => day.Key)
			.Select(day => new TimesheetDay(day.Key,
				day.Sum(slice => slice.Seconds),
				day
					.GroupBy(slice => slice.Task.Id)
					.Select(group => new TimesheetTask(group.Key,
						group.First().Task.ClientId(catalogue),
						group.First().Task.ProjectId,
						group.First().Task.Ref,
						group.Sum(slice => slice.Seconds)))
					.ToArray()))
			.ToArray();

		var projects = slices
			.GroupBy(slice => (ClientId: slice.Task.ClientId(catalogue), slice.Task.ProjectId))
			.Select(group => new TimesheetProjectTotal(group.Key.ClientId, group.Key.ProjectId, group.Sum(slice => slice.Seconds)))
			.ToArray();

		var clients = projects
			.GroupBy(project => project.ClientId)
			.Select(group => new TimesheetClientTotal(group.Key, group.Sum(project => project.RawSeconds)))
			.ToArray();

		return new(days, projects, clients);
	}

	private static DateTimeOffset Midnight(DateOnly date, TimeZoneInfo zone, DateTimeOffset? after = null)
	{
		// #35: search real instants rather than trusting IsInvalidTime. Entire skipped dates
		// (for example Apia in 2011) can round-trip to a different date without being marked invalid.
		var nominal = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).Ticks;
		var low = after?.UtcTicks ?? Math.Max(DateTimeOffset.MinValue.UtcTicks, nominal - 2 * TimeSpan.TicksPerDay);
		var high = Math.Min(DateTimeOffset.MaxValue.UtcTicks, (after?.UtcTicks ?? nominal) + 2 * TimeSpan.TicksPerDay);
		var first = new DateTimeOffset(low, TimeSpan.Zero);

		if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(first, zone).DateTime) >= date) return first;

		while (high - low > 1)
		{
			var middle = low + (high - low) / 2;
			var instant = new DateTimeOffset(middle, TimeSpan.Zero);
			var local = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

			if (local < date) low = middle;
			else high = middle;
		}

		// This finds the first occurrence of a repeated midnight, or the first real
		// instant after a midnight/date gap; a wholly skipped date has a zero-length day.
		return new(high, TimeSpan.Zero);
	}
}
