using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

/// <summary>Totals sum task-rounded figures and group by snapshot currency, even after a client currency changes.</summary>
public static class ReportMath
{
	public static IReadOnlyList<ReportTotal> Calculate(IEnumerable<TaskView> tasks, CatalogueView catalogue, ReportQuery query)
	{
		ArgumentNullException.ThrowIfNull(tasks);
		ArgumentNullException.ThrowIfNull(catalogue);
		ArgumentNullException.ThrowIfNull(query);
		var included = new List<(TaskView Task, Guid ClientId)>();

		foreach (var task in tasks)
		{
			if (task.State != TaskState.Stopped || task.Snapshot is null || task.StoppedAt is not { } stopped) continue;

			var clientId = task.ClientId(catalogue);

			if ((query.ClientId.HasValue && query.ClientId != clientId)
					|| (query.ProjectId.HasValue && query.ProjectId != task.ProjectId))
			{
				continue;
			}

			var settings = catalogue.Resolve(task.ProjectId);
			if (ReportPeriod.For(query, settings).Contains(stopped, settings.Timezone)) included.Add((task, clientId));
		}

		return included
			.GroupBy(x => (x.ClientId, x.Task.ProjectId, x.Task.Snapshot!.Currency))
			.Select(g => new ReportTotal(g.Key.ClientId,
				g.Key.ProjectId,
				g.Key.Currency,
				g.Sum(x => x.Task.RawSeconds),
				g.Sum(x => x.Task.RoundedSeconds),
				g.Any(x => x.Task.Snapshot!.Rate is null) ? null : g.Sum(x => TaskMath.Earnings(x.Task))))
			.OrderBy(x => x.ClientId)
			.ThenBy(x => x.ProjectId)
			.ThenBy(x => x.Currency, StringComparer.Ordinal)
			.ToArray();
	}
}

public sealed record ReportTotal(Guid ClientId, Guid ProjectId, string Currency, long RawSeconds, long RoundedSeconds, decimal? Earnings);
