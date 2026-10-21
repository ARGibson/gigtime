namespace Gigtime.Domain.Tasks;

internal static class TaskWarnings
{
	internal static IReadOnlyList<DecisionWarning>? RepeatedRef(Guid projectId,
		string? reference,
		OpenTask tracker,
		IReadOnlyList<TaskView> openTasks)
	{
		if (string.IsNullOrEmpty(reference)) return null;

		var repeated = openTasks
			.Where(task => tracker.TaskIds.Contains(task.Id)
				&& task.State is TaskState.Running or TaskState.Paused && task.ProjectId == projectId && task.Ref == reference)
			.Select(task => task.Id)
			.Order(StringComparer.Ordinal)
			.ToArray();

		return repeated.Length == 0
			? null
			: [new("repeated_ref", $"Reference '{reference}' repeats open task(s) {string.Join(", ", repeated)} in this project.")];
	}
}
