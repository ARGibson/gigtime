using System.Text.Json;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class AddedIntervalTests
{
	private static DateTimeOffset At => TaskScenario.At;

	[Fact]
	public void Added_intervals_keep_stable_numbers_but_sort_by_time_and_reopen_uses_the_next_number()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(At.AddMinutes(20)));
		s.Send(new ResumeTask(At.AddMinutes(40)));
		s.Send(new StopTask(At.AddMinutes(60)));
		s.Send(new AddInterval(At.AddMinutes(-60), At.AddMinutes(-30)));
		s.Send(new AddInterval(At.AddMinutes(20), At.AddMinutes(40)));
		s.Task.Timeline.Select(interval => interval.Number).ShouldBe([3, 1, 4, 2]);
		s.Task.StartedAt.ShouldBe(At.AddMinutes(-60));
		s.Task.StoppedAt.ShouldBe(At.AddMinutes(60));
		s.Task.RawSeconds.ShouldBe(90 * 60);
		s.Send(new AmendInterval(3, Start: At.AddMinutes(-50)));
		s.Task.Timeline[0].Number.ShouldBe(3);
		s.Send(new ResumeTask(At.AddMinutes(70)));
		s.Task.Timeline[^1].Number.ShouldBe(5);
		s.Send(new StopTask(At.AddMinutes(80)));
		s.Task.RawSeconds.ShouldBe(90 * 60);
		var replay = new TaskView();
		foreach (var e in s.TaskEvents) replay.Evolve(e);
		JsonSerializer.Serialize(replay).ShouldBe(JsonSerializer.Serialize(s.Task));
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	public void Added_time_is_allowed_in_each_editable_state_and_serialises_the_tracker(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var result = s.Decide(new AddInterval(At.AddMinutes(-2), At.AddMinutes(-1)));
		result.Error.ShouldBeNull();
		result.Events.OfType<IntervalAdded>().ShouldHaveSingleItem().ShouldBe(new(2, At.AddMinutes(-2), At.AddMinutes(-1)));
		result.Events.OfType<TrackerAdvanced>().ShouldHaveSingleItem();
		s.Send(new AddInterval(At.AddMinutes(-2), At.AddMinutes(-1)));
		s.Task.State.ShouldBe(state);
		s.Task.Timeline.Select(interval => interval.Number).ShouldBe([2, 1]);
	}

	[Theory]
	[InlineData(TaskState.Voided)]
	[InlineData(TaskState.Submitted)]
	public void Locked_tasks_refuse_added_time_without_events(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var result = s.Decide(new AddInterval(At.AddMinutes(-2), At.AddMinutes(-1)));
		result.Error!.Code.ShouldBe("invalid_transition");
		result.Events.ShouldBeEmpty();
		if (state == TaskState.Submitted) result.Error.Hint.ShouldBe("gig unsubmit 7f3a9c");
	}

	[Fact]
	public void Overlapping_and_backwards_intervals_refuse_while_touching_intervals_are_allowed()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(At.AddMinutes(10)));

		foreach (var command in new[]
						{
							new AddInterval(At.AddMinutes(5), At.AddMinutes(15)), new AddInterval(At.AddMinutes(-5), At.AddMinutes(5)),
							new AddInterval(At.AddMinutes(1), At.AddMinutes(2)), new AddInterval(At.AddMinutes(15), At.AddMinutes(10)),
						})
		{
			var result = s.Decide(command);
			result.Error!.Code.ShouldBe("validation");
			result.Events.ShouldBeEmpty();
		}

		s.Send(new AddInterval(At.AddMinutes(-10), At));
		s.Send(new AddInterval(At.AddMinutes(10), At.AddMinutes(20)));
		s.Task.Timeline.Select(interval => interval.Number).ShouldBe([2, 1, 3]);
		s.Task.RawSeconds.ShouldBe(30 * 60);
	}

	[Fact]
	public void Running_interval_stays_last_and_amend_checks_time_order_not_interval_numbers()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new AddInterval(At.AddMinutes(-10), At));
		var future = s.Decide(new AddInterval(At.AddMinutes(10), At.AddMinutes(20)));
		future.Error!.Code.ShouldBe("validation");
		future.Events.ShouldBeEmpty();
		s.Decide(new AmendInterval(2, End: At.AddMinutes(1))).Error!.Code.ShouldBe("validation");
		s.Send(new PauseTask(At.AddMinutes(5)));
		s.Task.Timeline.Select(interval => interval.Number).ShouldBe([2, 1]);
		s.Task.Timeline[^1].End.ShouldBe(At.AddMinutes(5));
		s.Send(new ResumeTask(At.AddMinutes(10)));
		s.Task.Timeline[^1].Number.ShouldBe(3);
	}
}
