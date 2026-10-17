using Gigtime.Domain.Catalogue;

namespace Gigtime.Domain.Tasks;

/// <summary>Self-aggregating task document. Zero-length spans retain their event numbers but leave the visible intervals.</summary>
public sealed class TaskView
{
	public string Id { get; set; } = "";
	public Guid ProjectId { get; set; }
	public string? Ref { get; set; }
	public TaskState State { get; set; }
	public List<Interval> Timeline { get; init; } = [];
	public IReadOnlyList<Interval> Intervals => Timeline.Where(i => i.End is null || i.End > i.Start).ToArray();
	public PolicySnapshot? Snapshot { get; set; }
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? StoppedAt { get; set; }
	public long RawSeconds => Timeline.Sum(i => i.Seconds);
	public long RoundedSeconds => Snapshot is null ? RawSeconds : TaskMath.Round(RawSeconds, Snapshot.Rounding);

	public long RawSecondsAt(DateTimeOffset now)
	{
		return RawSeconds + (State == TaskState.Running
			? Math.Max(0, (TaskMath.Instant(now) - Timeline[^1].Start).Ticks / TimeSpan.TicksPerSecond)
			: 0);
	}

	public Guid ClientId(CatalogueView catalogue) => catalogue.Projects[ProjectId].ClientId;

	public void Apply(TaskStarted e)
	{
		Id = e.TaskId;
		ProjectId = e.ProjectId;
		Ref = e.Ref;
		State = TaskState.Running;
		StartedAt = e.At;

		Timeline.Add(new(1, e.At, null));
	}

	public void Apply(TaskPaused e)
	{
		Close(e.At);
		State = TaskState.Paused;
	}

	public void Apply(TaskResumed e)
	{
		Timeline.Add(new(Timeline.Count + 1, e.At, null));
		State = TaskState.Running;
	}

	public void Apply(TaskStopped e)
	{
		if (State == TaskState.Running) Close(e.At);
		State = TaskState.Stopped;
		StoppedAt = e.At;
		Snapshot = new(e.Rounding, e.Rate, e.Currency);
	}

	public void Apply(TaskRecorded e)
	{
		Id = e.TaskId;
		ProjectId = e.ProjectId;
		Ref = e.Ref;
		State = TaskState.Stopped;
		StartedAt = e.Start;
		StoppedAt = e.End;
		Snapshot = new(e.Rounding, e.Rate, e.Currency);

		Timeline.Add(new(1, e.Start, e.End));
	}

	public void Apply(IntervalAmended e)
	{
		var index = Timeline.FindIndex(i => i.Number == e.Interval);
		var old = Timeline[index];

		Timeline[index] = old with
		{
			Start = e.Start ?? old.Start,
			End = e.End ?? old.End,
		};

		StartedAt = Timeline[0].Start;
		if (State == TaskState.Stopped) StoppedAt = Timeline[^1].End;
	}

	public void Apply(TaskReassigned e) => ProjectId = e.ProjectId;
	public void Apply(TaskRefChanged e) => Ref = e.Ref;
	public void Apply(TaskVoided _) => State = TaskState.Voided;

	public void Evolve(object e)
	{
		switch (e)
		{
			case TaskStarted x:
				Apply(x);

				break;
			case TaskPaused x:
				Apply(x);

				break;
			case TaskResumed x:
				Apply(x);

				break;
			case TaskStopped x:
				Apply(x);

				break;
			case TaskRecorded x:
				Apply(x);

				break;
			case IntervalAmended x:
				Apply(x);

				break;
			case TaskReassigned x:
				Apply(x);

				break;
			case TaskRefChanged x:
				Apply(x);

				break;
			case TaskVoided x:
				Apply(x);

				break;
			default: throw new ArgumentException("Not a task event.", nameof(e));
		}
	}

	private void Close(DateTimeOffset at)
	{
		Timeline[^1] = Timeline[^1] with
		{
			End = at,
		};
	}
}

public sealed record PolicySnapshot(string Rounding, decimal? Rate, string Currency);
