namespace Gigtime.Domain.Tasks;

internal static class TaskSwitch
{
	internal static Decision Decide(string targetId,
		OpenTask tracker,
		IReadOnlyList<TaskView> openTasks,
		string command,
		DateTimeOffset at,
		bool allowSwitch,
		string? switchFrom)
	{
		var running = openTasks
			.Where(task => tracker.TaskIds.Contains(task.Id) && task.State == TaskState.Running && task.Id != targetId)
			.ToArray();

		if (running.Length == 0) return Decision.Accept();

		if (running.Length != 1)
		{
			return Decision.Refuse("invalid_transition",
				"Several tasks are running; choose a task to pause.",
				"running",
				command,
				tasks: running.Select(task => task.Id).ToArray());
		}

		var other = running[0];

		if (switchFrom is not null && switchFrom != other.Id)
		{
			return Decision.Refuse("invalid_transition",
				$"Task {other.Id} is now running; it differs from the confirmed switch task {switchFrom}.",
				"running",
				command,
				"gig status",
				other.Id);
		}

		if (!allowSwitch)
		{
			return Decision.Refuse("invalid_transition",
				$"Task {other.Id} is running; use --switch to pause it.",
				"running",
				command,
				"Use --switch.",
				other.Id);
		}

		if (at < other.RunningStart())
			return Decision.Refuse("validation", "Switch cannot precede the running interval's start.", "running", command, taskId: other.Id);

		return Decision.Accept(new TaskEventFor(other.Id, new TaskPaused(at)));
	}
}
