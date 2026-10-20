using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

public static class SettlementMath
{
	public static SettlementResult Calculate(IEnumerable<TaskView> tasks, CatalogueView catalogue, ReportQuery query, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(tasks);
		ArgumentNullException.ThrowIfNull(catalogue);
		ArgumentNullException.ThrowIfNull(query);
		var submitted = new List<(TaskView Task, Guid ClientId, TaskFigures Figures)>();
		var unsubmitted = new List<UnsubmittedTask>();

		foreach (var task in ReportTasks.Select(tasks, catalogue, query))
		{
			var clientId = task.ClientId(catalogue);
			var figures = TaskFigures.Resolve(task, catalogue, now);

			if (task.State is TaskState.Running or TaskState.Paused or TaskState.Stopped)
			{
				unsubmitted.Add(new(task.Id, clientId, task.ProjectId, task.Ref, task.State, figures, task.Rate));

				continue;
			}

			var settings = catalogue.Resolve(task.ProjectId);

			if (task.State == TaskState.Submitted && task.Submission is { } submission
					&& ReportPeriod.For(query, settings).Contains(submission.SubmittedAt, settings.Timezone))
			{
				submitted.Add((task, clientId, figures));
			}
		}

		var totals = submitted
			.GroupBy(item => (item.ClientId, item.Task.ProjectId, item.Figures.Currency))
			.Select(group => new ReportTotal(group.Key.ClientId,
				group.Key.ProjectId,
				group.Key.Currency,
				group.Sum(item => item.Figures.RawSeconds),
				group.Sum(item => item.Figures.RoundedSeconds),
				group.Any(item => item.Figures.Rate is null) ? null : group.Sum(item => item.Figures.Earnings)))
			.OrderBy(total => catalogue.Clients[total.ClientId].Key, StringComparer.Ordinal)
			.ThenBy(total => catalogue.Projects[total.ProjectId].Key, StringComparer.Ordinal)
			.ThenBy(total => total.Currency, StringComparer.Ordinal)
			.ToArray();

		var clients = totals
			.GroupBy(total => (total.ClientId, total.Currency))
			.Select(group => new ClientReportTotal(group.Key.ClientId,
				group.Key.Currency,
				group.Sum(total => total.RawSeconds),
				group.Sum(total => total.RoundedSeconds),
				group.Any(total => total.Earnings is null) ? null : group.Sum(total => total.Earnings)))
			.OrderBy(total => catalogue.Clients[total.ClientId].Key, StringComparer.Ordinal)
			.ThenBy(total => total.Currency, StringComparer.Ordinal)
			.ToArray();

		return new(totals, clients, unsubmitted);
	}
}
