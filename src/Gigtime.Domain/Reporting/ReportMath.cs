using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

/// <summary>Totals use frozen submissions and current provisional figures, attributed by the task's stop time.</summary>
public static class ReportMath
{
	public static IReadOnlyList<ReportTotal> Calculate(IEnumerable<TaskView> tasks, CatalogueView catalogue, ReportQuery query)
	{
		ArgumentNullException.ThrowIfNull(tasks);
		ArgumentNullException.ThrowIfNull(catalogue);
		ArgumentNullException.ThrowIfNull(query);
		var included = new List<(TaskView Task, Guid ClientId, TaskFigures Figures)>();

		foreach (var task in tasks)
		{
			if (task.State is not (TaskState.Stopped or TaskState.Submitted) || task.StoppedAt is not { } stopped) continue;

			var clientId = task.ClientId(catalogue);

			if ((query.ClientId.HasValue && query.ClientId != clientId)
					|| (query.ProjectId.HasValue && query.ProjectId != task.ProjectId))
			{
				continue;
			}

			var settings = catalogue.Resolve(task.ProjectId);

			if (ReportPeriod.For(query, settings).Contains(stopped, settings.Timezone))
				included.Add((task, clientId, TaskFigures.Resolve(task, catalogue, stopped)));
		}

		return included
			.GroupBy(x => (x.ClientId, x.Task.ProjectId, x.Figures.Currency))
			.Select(g => new ReportTotal(g.Key.ClientId,
				g.Key.ProjectId,
				g.Key.Currency,
				g.Sum(x => x.Figures.RawSeconds),
				g.Sum(x => x.Figures.RoundedSeconds),
				g.Any(x => x.Figures.Rate is null) ? null : g.Sum(x => x.Figures.Earnings)))
			.OrderBy(x => x.ClientId)
			.ThenBy(x => x.ProjectId)
			.ThenBy(x => x.Currency, StringComparer.Ordinal)
			.ToArray();
	}
}

public sealed record ReportTotal(Guid ClientId, Guid ProjectId, string Currency, long RawSeconds, long RoundedSeconds, decimal? Earnings);
