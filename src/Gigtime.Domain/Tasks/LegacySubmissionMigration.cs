namespace Gigtime.Domain.Tasks;

public static class LegacySubmissionMigration
{
	public static TaskSubmitted? Decide(TaskView task)
	{
		ArgumentNullException.ThrowIfNull(task);

#pragma warning disable CS0618 // Only migration consumes the legacy snapshot.
		return task.State == TaskState.Stopped && task.Submission is null
			&& task.LegacySnapshot is { } snapshot && task.StoppedAt is { } stopped
				? new(stopped, TaskMath.Round(task.RawSeconds, snapshot.Rounding), snapshot.Rounding, snapshot.Rate, snapshot.Currency)
				: null;
#pragma warning restore CS0618
	}
}
