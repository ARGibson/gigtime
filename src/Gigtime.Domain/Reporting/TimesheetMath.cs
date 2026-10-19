using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using NodaTime;

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
			var zone = TimeZones.Get(settings.Timezone);
			var from = new LocalDate(period.From.Year, period.From.Month, period.From.Day);
			var until = new LocalDate(period.UntilExclusive.Year, period.UntilExclusive.Month, period.UntilExclusive.Day);

			foreach (var interval in task.Intervals)
			{
				var start = Instant.FromDateTimeOffset(interval.Start);
				var end = Instant.FromDateTimeOffset(interval.End ?? TaskMath.Instant(now));

				if (start >= end) continue;

				// #35: an offset change can move the local date backwards. TZDB supplies
				// constant-offset stretches; filter each piece by date, not UTC bounds.
				foreach (var stretch in zone.GetZoneIntervals(start, end))
				{
					var cursor = stretch.HasStart && stretch.Start > start ? stretch.Start : start;
					var limit = stretch.HasEnd && stretch.End < end ? stretch.End : end;

					while (cursor < limit)
					{
						var date = cursor.WithOffset(stretch.WallOffset).Date;

						var midnight = date == LocalDate.MaxIsoValue
							? limit
							: date.PlusDays(1).AtMidnight().WithOffset(stretch.WallOffset).ToInstant();

						var boundary = midnight < limit ? midnight : limit;
						var seconds = (boundary - cursor).BclCompatibleTicks / TimeSpan.TicksPerSecond;

						if (seconds > 0 && date >= from && date < until)
							slices.Add((new(date.Year, date.Month, date.Day), task, seconds));

						cursor = boundary;
					}
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
}
