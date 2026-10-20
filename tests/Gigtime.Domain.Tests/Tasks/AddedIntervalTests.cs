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

	[Fact]
	public void Same_second_pause_does_not_block_adding_and_amending_the_visible_interval()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(At.AddMilliseconds(900)));
		s.Task.Intervals.ShouldBeEmpty();
		s.Send(new AddInterval(At.AddMinutes(-1), At.AddMinutes(1)));
		s.Task.Intervals.ShouldHaveSingleItem().Number.ShouldBe(2);
		s.Send(new AmendInterval(2, Start: At.AddSeconds(-30)));
		s.Task.Intervals.ShouldHaveSingleItem().Start.ShouldBe(At.AddSeconds(-30));
		s.Task.Intervals[0].End.ShouldBe(At.AddMinutes(1));
		s.Task.RawSeconds.ShouldBe(90);
		s.Task.Timeline.Single(interval => interval.Number == 1).Seconds.ShouldBe(0);
	}

	[Fact]
	public void Zero_length_added_interval_refuses_without_events_but_new_backfill_can_still_be_empty()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(At.AddMinutes(10)));
		var result = s.Decide(new AddInterval(At.AddMinutes(5), At.AddMinutes(5)));
		result.Error!.Code.ShouldBe("validation");
		result.Events.ShouldBeEmpty();
		s.Task.Timeline.ShouldHaveSingleItem();
		var recorded = new TaskScenario();
		recorded.Send(new RecordTask("7f3a9c", recorded.Catalogue.ProjectId, null, At, At));
		recorded.Task.Intervals.ShouldBeEmpty();
		recorded.Task.Timeline.ShouldHaveSingleItem().Seconds.ShouldBe(0);
	}

	[Fact]
	public void A_legacy_zero_span_inside_a_visible_interval_does_not_block_amendment()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(At.AddMinutes(10)));
		// #36: earlier versions allowed a zero-length add inside another interval.
		var legacy = new IntervalAdded(2, At.AddMinutes(5), At.AddMinutes(5));
		s.Task.Evolve(legacy);
		s.TaskEvents.Add(legacy);
		s.Send(new AmendInterval(1, Start: At.AddMinutes(-1)));
		s.Task.Intervals.ShouldHaveSingleItem().Number.ShouldBe(1);
		s.Task.Intervals[0].Seconds.ShouldBe(11 * 60);
		s.Task.Timeline.Single(interval => interval.Number == 2).Seconds.ShouldBe(0);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void A_hidden_point_cannot_let_resume_or_stop_precede_the_latest_visible_endpoint(bool stop)
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(At));
		s.Send(new AddInterval(At.AddMinutes(-1), At.AddMinutes(1)));
		TaskCommand tooEarly = stop ? new StopTask(At.AddSeconds(30)) : new ResumeTask(At.AddSeconds(30));
		var refused = s.Decide(tooEarly);
		refused.Error!.Code.ShouldBe("validation");
		refused.Events.ShouldBeEmpty();
		TaskCommand atBoundary = stop ? new StopTask(At.AddMinutes(1)) : new ResumeTask(At.AddMinutes(1));
		s.Send(atBoundary);

		if (stop)
		{
			s.Task.StoppedAt.ShouldBe(At.AddMinutes(1));
		}
		else
		{
			s.Send(new PauseTask(At.AddMinutes(2)));
			s.Task.Intervals[^1].End.ShouldBe(At.AddMinutes(2));
		}
	}

	[Fact]
	public void Stopped_task_refreshes_its_end_from_the_latest_endpoint_instead_of_a_hidden_point()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(At));
		s.Send(new AddInterval(At.AddMinutes(-1), At.AddMinutes(1)));
		s.Task.Timeline[^1].Seconds.ShouldBe(0);
		s.Task.StoppedAt.ShouldBe(At.AddMinutes(1));
		s.Send(new AmendInterval(2, End: At.AddMinutes(2)));
		s.Task.StoppedAt.ShouldBe(At.AddMinutes(2));
		s.Decide(new ResumeTask(At.AddSeconds(90))).Error!.Code.ShouldBe("validation");
		s.Send(new ResumeTask(At.AddMinutes(2)));
		s.Task.Timeline.Single(interval => interval.End is null).Number.ShouldBe(3);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Running_figures_and_boundaries_use_the_open_interval_even_when_a_hidden_point_sorts_last(bool switching)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		s.Send("111111", new PauseTask(At));
		s.Send("111111", new ResumeTask(At.AddSeconds(10)));
		s.Send("111111", new AmendInterval(2, Start: At.AddSeconds(-10)));
		var task = s.Tasks["111111"];
		task.Timeline[^1].Number.ShouldBe(1);
		task.Timeline[^1].Seconds.ShouldBe(0);
		task.RawSecondsAt(At.AddSeconds(20)).ShouldBe(30);
		var overlap = s.Decide("111111", new AddInterval(At.AddSeconds(-5), At.AddSeconds(-1)));
		overlap.Error!.Code.ShouldBe("validation");
		overlap.Events.ShouldBeEmpty();

		if (switching) s.Start("222222", At.AddSeconds(20), switching: true);
		else s.Send("111111", new PauseTask(At.AddSeconds(20)));

		task.State.ShouldBe(TaskState.Paused);
		task.Timeline.Single(interval => interval.Number == 2).End.ShouldBe(At.AddSeconds(20));
		task.Timeline.Single(interval => interval.Number == 1).Seconds.ShouldBe(0);
		task.RawSeconds.ShouldBe(30);
	}
}
