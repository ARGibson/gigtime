namespace Gigtime.Domain.Tasks;

public static class LegacySubmissionMigration
{
	public static TaskSubmitted? Decide(TaskView task)
	{
		ArgumentNullException.ThrowIfNull(task);

		return task.State == TaskState.Stopped && task.Submission is null
			&& task.Snapshot is { } snapshot && task.StoppedAt is { } stopped
				? new(stopped, TaskMath.Round(task.RawSeconds, snapshot.Rounding), snapshot.Rounding, snapshot.Rate, snapshot.Currency)
				: null;
	}
}
