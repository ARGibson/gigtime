using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class LifecycleTests
{
	public static IEnumerable<object[]> Transitions()
	{
		foreach (var state in Enum.GetValues<TaskState>())
		{
			foreach (var command in new[] { "start", "pause", "resume", "stop", "void", "add", "submit", "unsubmit" })
				yield return [state, command];
		}
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
			"submit" => new SubmitTask(at),
			"unsubmit" => new UnsubmitTask(at),
			"add" => new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, at, at.AddMinutes(1)),
			_ => throw new InvalidOperationException()
		};

		var valid = command switch
		{
			"start" or "add" => state == TaskState.Missing,
			"pause" => state == TaskState.Running,
			"resume" => state is TaskState.Paused or TaskState.Stopped,
			"stop" => state is TaskState.Running or TaskState.Paused,
			"void" or "submit" => state == TaskState.Stopped,
			"unsubmit" => state == TaskState.Submitted,
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

			var expectedHint = (state, command) switch
			{
				(TaskState.Missing, _) => "gig start",
				(TaskState.Submitted, _) => "gig unsubmit " + s.Task.Id,
				(TaskState.Running or TaskState.Paused, "submit") => "gig stop --submit",
				(TaskState.Running or TaskState.Paused, "void" or "start" or "add") => "gig stop",
				(TaskState.Paused, "pause") => "gig resume",
				(TaskState.Stopped or TaskState.Voided, "start" or "add") => null,
				(TaskState.Stopped, "pause" or "resume" or "stop") => "gig start",
				(TaskState.Voided, _) => "gig start",
				_ => null
			};

			result.Error.Hint.ShouldBe(expectedHint);

			var expectedMessage = state switch
			{
				TaskState.Missing => "No task exists for this command.",
				TaskState.Running when command == "resume" => $"Task {s.Task.Id} is running; no resume is needed.",
				_ => $"Task {s.Task.Id} is {state.ToString().ToLowerInvariant()}; {command} does not apply." +
					(expectedHint is null ? "" : $" Use {expectedHint}.")
			};

			result.Error.Message.ShouldBe(expectedMessage);

			if (state is TaskState.Stopped or TaskState.Voided && command is "start" or "add")
				result.Error.Message.ShouldNotContain("Use gig start");
		}
	}

	[Fact]
	public void Paused_task_remains_open_while_another_task_can_start_and_stop_removes_only_the_target()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));

		s.Tracker.TaskIds.ShouldContain(s.Task.Id);

		var another = new TaskView();

		var started = TaskDecider.Decide(another,
			s.Tracker,
			s.Catalogue.State,
			new StartTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At),
			[s.Task]);

		started.Error.ShouldBeNull();
		started.Events.OfType<TaskOpened>().ShouldHaveSingleItem().TaskId.ShouldBe("123abc");

		s.Send(new StopTask(TaskScenario.At.AddMinutes(1)));

		s.Tracker.TaskIds.ShouldBeEmpty();
		s.Task.RawSeconds.ShouldBe(20);
		s.Task.StoppedAt.ShouldBe(TaskScenario.At.AddMinutes(1));

		TaskDecider
			.Decide(another,
				s.Tracker,
				s.Catalogue.State,
				new StartTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At.AddMinutes(2)),
				[])
			.IsSuccess.ShouldBeTrue();
	}

	[Fact]
	public void Pause_resume_stop_numbers_intervals_and_ignores_paused_time()
	{
		var s = new TaskScenario();
		s.Start();

		s.Task.RawSecondsAt(TaskScenario.At.AddSeconds(10)).ShouldBe(10);

		s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
		s.Send(new ResumeTask(TaskScenario.At.AddMinutes(2)));
		s.Send(new StopTask(TaskScenario.At.AddMinutes(2).AddSeconds(20)));

		s.Task.Intervals.Select(x => x.Number).ShouldBe([1, 2]);
		s.Task.RawSeconds.ShouldBe(40);
		s.Figures.RoundedSeconds.ShouldBe(60);
		s.Task.State.ShouldBe(TaskState.Stopped);
		s.Tracker.TaskIds.ShouldBeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Same_second_pause_or_stop_drops_the_zero_interval(bool pause)
	{
		var s = new TaskScenario();
		s.Start();

		if (pause) s.Send(new PauseTask(TaskScenario.At.AddMilliseconds(900)));
		else s.Send(new StopTask(TaskScenario.At.AddMilliseconds(900)));

		s.Task.Intervals.ShouldBeEmpty();
		s.Task.RawSeconds.ShouldBe(0);

		if (pause)
		{
			s.Send(new ResumeTask(TaskScenario.At.AddSeconds(2)));
			s.Send(new StopTask(TaskScenario.At.AddSeconds(32)));
			s.Task.Intervals.Single().Number.ShouldBe(2);
			s.Figures.RoundedSeconds.ShouldBe(60);
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
	public void Evolve_is_deterministic_for_task_and_tracker_events()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(TaskScenario.At.AddSeconds(10)));
		s.Send(new ResumeTask(TaskScenario.At.AddSeconds(20)));
		s.Send(new ChangeTaskRef(null));
		s.Send(new StopTask(TaskScenario.At.AddSeconds(40)));
		s.Send(new AmendInterval(2, End: TaskScenario.At.AddSeconds(50)));
		s.Send(new VoidTask("duplicate"));
		var task = new TaskView();
		var tracker = new OpenTask();
		foreach (var e in s.TaskEvents) task.Evolve(e);
		foreach (var e in s.TrackerEvents) tracker.Evolve(e);

		task.Id.ShouldBe(s.Task.Id);
		task.ProjectId.ShouldBe(s.Task.ProjectId);
		task.ClientId(s.Catalogue.State).ShouldBe(s.Catalogue.ClientId);
		task.Ref.ShouldBeNull();
		task.State.ShouldBe(s.Task.State);
		task.Timeline.ShouldBe(s.Task.Timeline);
		task.Rate.ShouldBe(s.Task.Rate);
		task.Submission.ShouldBe(s.Task.Submission);
		task.StartedAt.ShouldBe(s.Task.StartedAt);
		task.StoppedAt.ShouldBe(s.Task.StoppedAt);
		task.RawSeconds.ShouldBe(s.Task.RawSeconds);
		TaskFigures.Resolve(task, s.Catalogue.State, TaskScenario.At).ShouldBe(s.Figures);
		tracker.TaskIds.ShouldBe(s.Tracker.TaskIds);
	}
}
