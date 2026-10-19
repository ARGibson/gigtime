using Gigtime.Domain.Catalogue;

namespace Gigtime.Domain.Tasks;

/// <summary>Pure decisions; the host routes tracker events separately and commits both streams atomically.</summary>
public static class TaskDecider
{
	public static Decision Decide(TaskView task, OpenTask tracker, CatalogueView catalogue, TaskCommand command)
	{
		ArgumentNullException.ThrowIfNull(task);
		ArgumentNullException.ThrowIfNull(tracker);
		ArgumentNullException.ThrowIfNull(catalogue);
		ArgumentNullException.ThrowIfNull(command);

		if (task.State == TaskState.Submitted)
		{
			return command is UnsubmitTask unsubmit
				? Decision.Accept(new TaskUnsubmitted(TaskMath.Instant(unsubmit.At)))
				: Transition(task, command);
		}

		if (command is StartTask start) return Start(task, tracker, catalogue, start);
		if (command is RecordTask record) return Record(task, catalogue, record);

		if (task.State == TaskState.Missing)
			return Decision.Refuse("not_found", "No task exists for this command.", "missing", command.Command, "gig start");

		return command switch
		{
			PauseTask c when task.State == TaskState.Running => Close(task, tracker, catalogue, c, c.At, false),
			ResumeTask c when task.State == TaskState.Paused => Resume(task, tracker, c),
			StopTask c when task.State is TaskState.Running or TaskState.Paused => Close(task, tracker, catalogue, c, c.At, true),
			VoidTask c when task.State == TaskState.Stopped => Decision.Accept(new TaskVoided(c.Reason)),
			AmendInterval c when task.State != TaskState.Voided => Amend(task, c),
			ReassignTask c when task.State != TaskState.Voided => Reassign(task, catalogue, c),
			ChangeTaskRef c when task.State != TaskState.Voided => Decision.Accept(new TaskRefChanged(c.Ref)),
			SetTaskRate c when task.State != TaskState.Voided => SetRate(task, catalogue, c),
			SubmitTask c when task.State == TaskState.Stopped => Submit(task, catalogue, c),
			_ => Transition(task, command),
		};
	}

	private static Decision Start(TaskView task, OpenTask tracker, CatalogueView catalogue, StartTask c)
	{
		if (task.State != TaskState.Missing) return Transition(task, c);

		if (tracker.TaskId is { } open)
		{
			return Decision.Refuse("invalid_transition",
				$"The open task {open} must be stopped before starting another.",
				"open",
				c.Command,
				"gig stop");
		}

		if (!TaskIds.IsValid(c.TaskId)) return Invalid(task, c, "Use a six-character lowercase hex task id.");
		if (ProjectRefusal(catalogue, c.ProjectId, c) is { } error) return error;

		if (c.Rate < 0) return Invalid(task, c, "Task rate cannot be negative.");

		var events = new List<object>
		{
			new TaskStarted(c.TaskId, c.ProjectId, c.Ref, TaskMath.Instant(c.At))
		};

		if (c.Rate is { } rate) events.Add(new TaskRateSet(rate, catalogue.Resolve(c.ProjectId).Currency));
		events.Add(new OpenTaskSet(c.TaskId));

		return Decision.Accept(events.ToArray());
	}

	private static Decision Record(TaskView task, CatalogueView catalogue, RecordTask c)
	{
		if (task.State != TaskState.Missing) return Transition(task, c);

		if (!TaskIds.IsValid(c.TaskId) || TaskMath.Instant(c.End) < TaskMath.Instant(c.Start))
			return Invalid(task, c, "Use a new task id and an end at or after the start.");

		if (ProjectRefusal(catalogue, c.ProjectId, c) is { } error) return error;

		if (c.Rate < 0) return Invalid(task, c, "Task rate cannot be negative.");

		var events = new List<object>
		{
			new TaskRecorded(c.TaskId, c.ProjectId, c.Ref, TaskMath.Instant(c.Start), TaskMath.Instant(c.End)),
		};

		if (c.Rate is { } rate) events.Add(new TaskRateSet(rate, catalogue.Resolve(c.ProjectId).Currency));

		return Decision.Accept(events.ToArray());
	}

	private static Decision Close(TaskView task, OpenTask tracker, CatalogueView catalogue, TaskCommand c, DateTimeOffset instant, bool stop)
	{
		if (tracker.TaskId != task.Id) return Transition(task, c);

		var at = TaskMath.Instant(instant);
		var last = task.Timeline[^1];

		if (at < (last.End ?? last.Start)) return Invalid(task, c, "The instant precedes the last interval boundary.");
		if (!stop) return Decision.Accept(new TaskPaused(at));

		if (c is StopTask { Submit: true })
		{
			if (RateCurrencyRefusal(task, catalogue, c) is { } refusal) return refusal;

			var figures = TaskFigures.Resolve(task, catalogue, at);

			return Decision.Accept(new TaskStopped(at), Submitted(at, figures), new OpenTaskCleared());
		}

		return Decision.Accept(new TaskStopped(at), new OpenTaskCleared());
	}

	private static TaskSubmitted Submitted(DateTimeOffset at, TaskFigures figures)
	{
		return new(at, figures.RoundedSeconds, figures.Rounding, figures.Rate, figures.Currency);
	}

	private static Decision Submit(TaskView task, CatalogueView catalogue, SubmitTask c)
	{
		if (RateCurrencyRefusal(task, catalogue, c) is { } refusal) return refusal;

		var at = TaskMath.Instant(c.At);

		return Decision.Accept(Submitted(at, TaskFigures.Resolve(task, catalogue, at)));
	}

	private static Decision? RateCurrencyRefusal(TaskView task, CatalogueView catalogue, TaskCommand c)
	{
		return task.Rate is { } rate && rate.Currency != catalogue.Resolve(task.ProjectId).Currency
			? Invalid(task, c, "The task rate currency differs from the client's currency; amend or clear the task rate first.")
			: null;
	}

	private static Decision SetRate(TaskView task, CatalogueView catalogue, SetTaskRate c)
	{
		if (c.Rate < 0) return Invalid(task, c, "Task rate cannot be negative.");

		return Decision.Accept(new TaskRateSet(c.Rate, c.Rate is null ? null : catalogue.Resolve(task.ProjectId).Currency));
	}

	private static Decision Resume(TaskView task, OpenTask tracker, ResumeTask c)
	{
		if (tracker.TaskId != task.Id) return Transition(task, c);

		var at = TaskMath.Instant(c.At);

		if (at < task.Timeline[^1].End) return Invalid(task, c, "Resume cannot precede the last interval endpoint.");

		return Decision.Accept(new TaskResumed(at));
	}

	private static Decision Reassign(TaskView task, CatalogueView catalogue, ReassignTask c)
	{
		if (ProjectRefusal(catalogue, c.ProjectId, c) is { } error) return error;
		if (c.Rate.IsSpecified && c.Rate.Value < 0) return Invalid(task, c, "Task rate cannot be negative.");

		var currency = catalogue.Resolve(c.ProjectId).Currency;

		if (task.Rate is { } rate && rate.Currency != currency && !c.Rate.IsSpecified)
			return Invalid(task, c, "Moving this task to another currency requires a new task rate or clearing its rate.");

		return c.Rate.IsSpecified
			? Decision.Accept(new TaskReassigned(c.ProjectId), new TaskRateSet(c.Rate.Value, c.Rate.Value is null ? null : currency))
			: Decision.Accept(new TaskReassigned(c.ProjectId));
	}

	private static Decision Amend(TaskView task, AmendInterval c)
	{
		if (!c.Start.HasValue && !c.End.HasValue) return Invalid(task, c, "Specify an interval start or end to amend.");

		var index = task.Timeline.FindIndex(i => i.Number == c.Interval);

		if (index < 0) return Decision.Refuse("not_found", "No such interval.", State(task), c.Command);

		var intervals = task.Timeline.ToArray();
		var old = intervals[index];

		if (old.End is null && c.End.HasValue) return Invalid(task, c, "Pause or stop before closing the running interval.");

		intervals[index] = old with
		{
			Start = c.Start is { } start ? TaskMath.Instant(start) : old.Start,
			End = c.End is { } end ? TaskMath.Instant(end) : old.End,
		};

		for (var i = 0; i < intervals.Length; i++)
		{
			var interval = intervals[i];

			if (interval.End < interval.Start || (i > 0 && intervals[i - 1].End > interval.Start))
				return Invalid(task, c, "Intervals must remain ordered and non-overlapping.");
		}

		return Decision.Accept(new IntervalAmended(c.Interval,
			c.Start is { } s ? TaskMath.Instant(s) : null,
			c.End is { } e ? TaskMath.Instant(e) : null));
	}

	private static Decision? ProjectRefusal(CatalogueView catalogue, Guid id, TaskCommand c)
	{
		var p = catalogue.FindProject(id);

		if (p is null) return Decision.Refuse("not_found", "The project does not exist.", "catalogue", c.Command, "gig project list");

		if (p.Archived || catalogue.Clients[p.ClientId].Archived)
			return Decision.Refuse("validation", "The client or project is archived.", "archived", c.Command);

		return null;
	}

	private static string State(TaskView task) => task.State.ToString().ToLowerInvariant();

	private static Decision Invalid(TaskView task, TaskCommand c, string message)
	{
		return Decision.Refuse("validation", message, State(task), c.Command);
	}

	private static Decision Transition(TaskView task, TaskCommand c)
	{
		var hint = (task.State, c) switch
		{
			(TaskState.Submitted, _) => "gig unsubmit " + task.Id,
			(TaskState.Running or TaskState.Paused, SubmitTask) => "gig stop --submit",
			(TaskState.Running or TaskState.Paused, StartTask or RecordTask or VoidTask) => "gig stop",
			(TaskState.Paused, PauseTask) => "gig resume",
			(TaskState.Stopped or TaskState.Voided, StartTask or RecordTask) => null,
			(TaskState.Stopped, PauseTask or ResumeTask or StopTask) => "gig start",
			(TaskState.Voided, _) => "gig start",
			_ => null,
		};

		var message = (task.State, c) switch
		{
			(TaskState.Running, ResumeTask) => $"Task {task.Id} is running; no resume is needed.",
			_ => $"Task {task.Id} is {State(task)}; {c.Command} does not apply." + (hint is null ? "" : $" Use {hint}."),
		};

		return Decision.Refuse("invalid_transition", message, State(task), c.Command, hint);
	}
}
