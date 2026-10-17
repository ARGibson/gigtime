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
	  _ => Transition(task, command)
	};
  }

  private static Decision Start(TaskView task, OpenTask tracker, CatalogueView catalogue, StartTask c)
  {
	if (task.State != TaskState.Missing) return Transition(task, c);
	if (tracker.TaskId is { } open)
	  return Decision.Refuse("invalid_transition", $"The open task {open} must be stopped before starting another.", "open", c.Command, "gig stop");
	if (!TaskIds.IsValid(c.TaskId)) return Invalid(task, c, "Use a six-character lowercase hex task id.");
	if (ProjectRefusal(catalogue, c.ProjectId, c) is { } error) return error;
	return Decision.Accept(new TaskStarted(c.TaskId, c.ProjectId, c.Ref, TaskMath.Instant(c.At)), new OpenTaskSet(c.TaskId));
  }

  private static Decision Record(TaskView task, CatalogueView catalogue, RecordTask c)
  {
	if (task.State != TaskState.Missing) return Transition(task, c);
	if (!TaskIds.IsValid(c.TaskId) || TaskMath.Instant(c.End) < TaskMath.Instant(c.Start))
	  return Invalid(task, c, "Use a new task id and an end at or after the start.");
	if (ProjectRefusal(catalogue, c.ProjectId, c) is { } error) return error;
	var policy = catalogue.Resolve(c.ProjectId);
	return Decision.Accept(new TaskRecorded(c.TaskId, c.ProjectId, c.Ref, TaskMath.Instant(c.Start), TaskMath.Instant(c.End), policy.Rounding, policy.Rate, policy.Currency));
  }

  private static Decision Close(TaskView task, OpenTask tracker, CatalogueView catalogue, TaskCommand c, DateTimeOffset instant, bool stop)
  {
	if (tracker.TaskId != task.Id) return Transition(task, c);
	var at = TaskMath.Instant(instant);
	var last = task.Timeline[^1];
	if (at < (last.End ?? last.Start)) return Invalid(task, c, "The instant precedes the last interval boundary.");
	if (!stop) return Decision.Accept(new TaskPaused(at));
	var policy = catalogue.Resolve(task.ProjectId);
	return Decision.Accept(new TaskStopped(at, policy.Rounding, policy.Rate, policy.Currency), new OpenTaskCleared());
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
	return Decision.Accept(new TaskReassigned(c.ProjectId));
  }

  private static Decision Amend(TaskView task, AmendInterval c)
  {
	var index = task.Timeline.FindIndex(i => i.Number == c.Interval);
	if (index < 0) return Decision.Refuse("not_found", "No such interval.", State(task), c.Command);
	var intervals = task.Timeline.ToArray();
	var old = intervals[index];
	if (old.End is null && c.End.HasValue) return Invalid(task, c, "Pause or stop before closing the running interval.");
	intervals[index] = old with { Start = c.Start is { } start ? TaskMath.Instant(start) : old.Start, End = c.End is { } end ? TaskMath.Instant(end) : old.End };
	for (var i = 0; i < intervals.Length; i++)
	{
	  var interval = intervals[i];
	  if (interval.End < interval.Start || (i > 0 && intervals[i - 1].End > interval.Start))
		return Invalid(task, c, "Intervals must remain ordered and non-overlapping.");
	}
	return Decision.Accept(new IntervalAmended(c.Interval, c.Start is { } s ? TaskMath.Instant(s) : null, c.End is { } e ? TaskMath.Instant(e) : null));
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
  private static Decision Invalid(TaskView task, TaskCommand c, string message) => Decision.Refuse("validation", message, State(task), c.Command);
  private static Decision Transition(TaskView task, TaskCommand c)
  {
	var hint = task.State switch { TaskState.Running => "gig pause", TaskState.Paused => "gig resume", TaskState.Stopped => "gig start", _ => "gig start" };
	return Decision.Refuse("invalid_transition", $"Task {task.Id} is {State(task)}; {c.Command} does not apply — use {hint}.", State(task), c.Command, hint);
  }
}
