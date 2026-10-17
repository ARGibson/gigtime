using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class LifecycleTests
{
  public static IEnumerable<object[]> Transitions()
  {
	foreach (var state in Enum.GetValues<TaskState>())
	  foreach (var command in new[] { "start", "pause", "resume", "stop", "void", "add" })
		yield return [state, command];
  }

  [Theory]
  [MemberData(nameof(Transitions))]
  public void Every_lifecycle_command_accepts_only_its_defined_states(TaskState state, string command)
  {
	var s = new TaskScenario();
	s.Reach(state);
	var at = TaskScenario.At.AddMinutes(1);
	TaskCommand c = command switch
	{
	  "start" => new StartTask("7f3a9c", s.Catalogue.ProjectId, null, at),
	  "pause" => new PauseTask(at),
	  "resume" => new ResumeTask(at),
	  "stop" => new StopTask(at),
	  "void" => new VoidTask(),
	  "add" => new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, at, at.AddMinutes(1)),
	  _ => throw new InvalidOperationException()
	};
	var valid = command switch
	{
	  "start" or "add" => state == TaskState.Missing,
	  "pause" => state == TaskState.Running,
	  "resume" => state == TaskState.Paused,
	  "stop" => state is TaskState.Running or TaskState.Paused,
	  "void" => state == TaskState.Stopped,
	  _ => false
	};
	var result = s.Decide(c);
	result.IsSuccess.ShouldBe(valid);
	if (!valid)
	{
	  result.Events.ShouldBeEmpty();
	  result.Error!.Code.ShouldBe(state == TaskState.Missing ? "not_found" : "invalid_transition");
	  result.Error.State.ShouldBe(state.ToString().ToLowerInvariant());
	  result.Error.Command.ShouldBe(command);
	}
  }

  [Fact]
  public void Paused_task_keeps_the_open_slot_and_stop_releases_it()
  {
	var s = new TaskScenario(); s.Start();
	s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
	s.Tracker.TaskId.ShouldBe(s.Task.Id);
	var another = new TaskView();
	var refusal = TaskDecider.Decide(another, s.Tracker, s.Catalogue.State,
		new StartTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At));
	refusal.Error!.Code.ShouldBe("invalid_transition");
	refusal.Events.ShouldBeEmpty();
	s.Send(new StopTask(TaskScenario.At.AddMinutes(1)));
	s.Tracker.TaskId.ShouldBeNull();
	s.Task.RawSeconds.ShouldBe(20);
	s.Task.StoppedAt.ShouldBe(TaskScenario.At.AddMinutes(1));
	TaskDecider.Decide(another, s.Tracker, s.Catalogue.State,
		new StartTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At.AddMinutes(2))).IsSuccess.ShouldBeTrue();
  }

  [Fact]
  public void Pause_resume_stop_numbers_intervals_and_ignores_paused_time()
  {
	var s = new TaskScenario(); s.Start();
	s.Task.RawSecondsAt(TaskScenario.At.AddSeconds(10)).ShouldBe(10);
	s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
	s.Send(new ResumeTask(TaskScenario.At.AddMinutes(2)));
	s.Send(new StopTask(TaskScenario.At.AddMinutes(2).AddSeconds(20)));
	s.Task.Intervals.Select(x => x.Number).ShouldBe([1, 2]);
	s.Task.RawSeconds.ShouldBe(40);
	s.Task.RoundedSeconds.ShouldBe(60);
	s.Task.State.ShouldBe(TaskState.Stopped);
	s.Tracker.TaskId.ShouldBeNull();
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public void Same_second_pause_or_stop_drops_the_zero_interval(bool pause)
  {
	var s = new TaskScenario(); s.Start();
	if (pause) s.Send(new PauseTask(TaskScenario.At.AddMilliseconds(900)));
	else s.Send(new StopTask(TaskScenario.At.AddMilliseconds(900)));
	s.Task.Intervals.ShouldBeEmpty();
	s.Task.RawSeconds.ShouldBe(0);
	if (pause)
	{
	  s.Send(new ResumeTask(TaskScenario.At.AddSeconds(2)));
	  s.Send(new StopTask(TaskScenario.At.AddSeconds(32)));
	  s.Task.Intervals.Single().Number.ShouldBe(2);
	  s.Task.RoundedSeconds.ShouldBe(60);
	}
  }

  [Fact]
  public void Id_allocation_retries_collisions_and_enforces_six_lowercase_hex_digits()
  {
	var ids = new Queue<string>(["7f3a9c", "7f3a9c", "123abc"]);
	TaskIds.Create(x => x == "7f3a9c", ids.Dequeue).ShouldBe("123abc");
	TaskIds.IsValid(TaskIds.Create(_ => false)).ShouldBeTrue();
	TaskIds.IsValid("ABCDEF").ShouldBeFalse();
	TaskIds.IsValid("12345").ShouldBeFalse();
  }

  [Fact]
  public void Replay_matches_both_task_and_tracker_documents()
  {
	var s = new TaskScenario(); s.Start();
	s.Send(new PauseTask(TaskScenario.At.AddSeconds(10)));
	s.Send(new ResumeTask(TaskScenario.At.AddSeconds(20)));
	s.Send(new ChangeTaskRef(null));
	s.Send(new StopTask(TaskScenario.At.AddSeconds(40)));
	s.Send(new VoidTask("duplicate"));
	var task = new TaskView(); var tracker = new OpenTask();
	foreach (var e in s.TaskEvents) task.Evolve(e);
	foreach (var e in s.TrackerEvents) tracker.Evolve(e);
	task.Id.ShouldBe(s.Task.Id);
	task.ProjectId.ShouldBe(s.Task.ProjectId);
	task.ClientId(s.Catalogue.State).ShouldBe(s.Catalogue.ClientId);
	task.Ref.ShouldBeNull();
	task.State.ShouldBe(s.Task.State);
	task.Timeline.ShouldBe(s.Task.Timeline);
	task.Snapshot.ShouldBe(s.Task.Snapshot);
	task.StartedAt.ShouldBe(s.Task.StartedAt);
	task.StoppedAt.ShouldBe(s.Task.StoppedAt);
	task.RawSeconds.ShouldBe(s.Task.RawSeconds);
	task.RoundedSeconds.ShouldBe(s.Task.RoundedSeconds);
	tracker.TaskId.ShouldBe(s.Tracker.TaskId);
  }
}
