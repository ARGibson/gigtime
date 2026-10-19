using System.Text.Json.Serialization;
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

	// #34: retained only to recognise legacy events for the one-off submission migration.
	[Obsolete("Exists only for the legacy submission migration.")]
	[JsonPropertyName("snapshot")]
	public PolicySnapshot? LegacySnapshot { get; set; }

	public TaskRate? Rate { get; set; }
	public TaskSubmission? Submission { get; set; }
	public DateTimeOffset? StartedAt { get; set; }
	public DateTimeOffset? StoppedAt { get; set; }
	public long RawSeconds => Timeline.Sum(i => i.Seconds);

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
#pragma warning disable CS0618 // Legacy event projection retains the migration input.
		LegacySnapshot = e.Rounding is { } rounding && e.Currency is { } currency ? new(rounding, e.Rate, currency) : null;
#pragma warning restore CS0618
	}

	public void Apply(TaskRecorded e)
	{
		Id = e.TaskId;
		ProjectId = e.ProjectId;
		Ref = e.Ref;
		State = TaskState.Stopped;
		StartedAt = e.Start;
		StoppedAt = e.End;
#pragma warning disable CS0618 // Legacy event projection retains the migration input.
		LegacySnapshot = e.Rounding is { } rounding && e.Currency is { } currency ? new(rounding, e.Rate, currency) : null;
#pragma warning restore CS0618

		Timeline.Add(new(1, e.Start, e.End));
	}

	public void Apply(TaskSubmitted e)
	{
		Submission = new(e.At, e.RoundedSeconds, e.Rounding, e.Rate, e.Currency);
		State = TaskState.Submitted;
	}

	public void Apply(TaskUnsubmitted _)
	{
		Submission = null;
		State = TaskState.Stopped;
	}

	public void Apply(TaskRateSet e) => Rate = e.Rate is { } amount && e.Currency is { } currency ? new(amount, currency) : null;

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
			case TaskSubmitted x:
				Apply(x);

				break;
			case TaskUnsubmitted x:
				Apply(x);

				break;
			case TaskRateSet x:
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
