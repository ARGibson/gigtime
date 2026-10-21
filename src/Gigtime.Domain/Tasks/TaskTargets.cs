namespace Gigtime.Domain.Tasks;

public static class TaskTargets
{
	public static TaskTarget Choose(string command, OpenTask tracker, IReadOnlyList<TaskView> openViews)
	{
		var open = openViews.Where(task => tracker.TaskIds.Contains(task.Id)).ToArray();
		var running = open.Where(task => task.State == TaskState.Running).ToArray();

		if (command == "resume" && running.Length != 0)
		{
			return new(null,
				new("invalid_transition",
					$"Task {running[0].Id} is running; choose a task id to switch.",
					"running",
					command,
					"gig status",
					running[0].Id));
		}

		var eligible = command switch
		{
			"pause" => running,
			"stop" when running.Length != 0 => running,
			"stop" or "resume" => Paused(open),
			_ => throw new ArgumentException("Choose pause, resume or stop.", nameof(command)),
		};

		return eligible.Count switch
		{
			1 => new(eligible[0].Id),
			0 => new(null, new("invalid_transition", $"No eligible task for {command}.", "none", command, "gig status")),
			_ => new(null,
				new("invalid_transition",
					$"Several tasks are eligible for {command}; choose a task id.",
					"ambiguous",
					command,
					"gig status",
					Tasks: eligible.Select(task => task.Id).ToArray())),
		};
	}

	public static IReadOnlyList<TaskView> Paused(IReadOnlyList<TaskView> openViews) => openViews
		.Where(task => task.State == TaskState.Paused)
		.OrderByDescending(task => task.PausedAt ?? task.Timeline.LastOrDefault()?.End)
		.ThenBy(task => task.Id, StringComparer.Ordinal)
		.ToArray();
}

public sealed record TaskTarget(string? TaskId, Rejection? Error = null);
