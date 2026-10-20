using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

internal static class ReportTasks
{
	internal static TaskView[] Select(IEnumerable<TaskView> tasks, CatalogueView catalogue, ReportQuery query)
	{
		return tasks
			.Where(task => task.State is not (TaskState.Missing or TaskState.Voided))
			.Where(task => !query.ClientId.HasValue || task.ClientId(catalogue) == query.ClientId)
			.Where(task => !query.ProjectId.HasValue || task.ProjectId == query.ProjectId)
			.OrderBy(task => catalogue.Clients[task.ClientId(catalogue)].Key, StringComparer.Ordinal)
			.ThenBy(task => catalogue.Projects[task.ProjectId].Key, StringComparer.Ordinal)
			.ThenBy(task => task.Timeline[0].Start)
			.ThenBy(task => task.Id, StringComparer.Ordinal)
			.ToArray();
	}
}
