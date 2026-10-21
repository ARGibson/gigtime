using System.Text.Json;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class OpenTasksTests
{
	private static DateTimeOffset At => TaskScenario.At;

	[Fact]
	public void Switch_pauses_the_other_task_atomically_then_bare_stop_can_stop_each_task()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		var before = JsonSerializer.Serialize(s.Tasks["111111"]);
		var planned = s.Decide("222222", new StartTask("222222", s.Seed.Catalogue.ProjectId, null, At.AddMinutes(1), Switch: true));
		planned.Error.ShouldBeNull();
		planned.Events[0].ShouldBe(new TaskEventFor("111111", new TaskPaused(At.AddMinutes(1))));
		planned.Events.OfType<TaskOpened>().ShouldHaveSingleItem().TaskId.ShouldBe("222222");
		JsonSerializer.Serialize(s.Tasks["111111"]).ShouldBe(before);
		s.Start("222222", At.AddMinutes(1), switching: true);
		s.Tasks["111111"].State.ShouldBe(TaskState.Paused);
		s.Tasks["111111"].PausedAt.ShouldBe(At.AddMinutes(1));
		s.Tasks["222222"].State.ShouldBe(TaskState.Running);
		s.Tracker.TaskIds.ShouldBe(["111111", "222222"]);
		TaskTargets.Choose("stop", s.Tracker, s.OpenViews).TaskId.ShouldBe("222222");
		s.Send("222222", new StopTask(At.AddMinutes(2)));
		TaskTargets.Choose("stop", s.Tracker, s.OpenViews).TaskId.ShouldBe("111111");
		s.Send("111111", new StopTask(At.AddMinutes(3)));
		s.Tracker.TaskIds.ShouldBeEmpty();
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Start_and_resume_without_switch_refuse_with_the_running_id_and_no_events(bool resume)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		s.Send("111111", new PauseTask(At.AddSeconds(10)));
		s.Start("222222", At.AddSeconds(20));
		var target = resume ? "111111" : "333333";

		TaskCommand command = resume
			? new ResumeTask(At.AddSeconds(30))
			: new StartTask(target, s.Seed.Catalogue.ProjectId, null, At.AddSeconds(30));

		var before = JsonSerializer.Serialize(s.Tracker);
		var result = s.Decide(target, command);
		result.Events.ShouldBeEmpty();
		result.Error!.Code.ShouldBe("invalid_transition");
		result.Error.TaskId.ShouldBe("222222");
		result.Error.State.ShouldBe("running");
		result.Error.Message.ShouldContain("222222");
		JsonSerializer.Serialize(s.Tracker).ShouldBe(before);
		s.Tasks["222222"].State.ShouldBe(TaskState.Running);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Resume_can_switch_to_a_paused_or_stopped_task(bool stopped)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);

		if (stopped) s.Send("111111", new StopTask(At.AddSeconds(20)));
		else s.Send("111111", new PauseTask(At.AddSeconds(20)));

		s.Start("222222", At.AddSeconds(30));
		var result = s.Send("111111", new ResumeTask(At.AddSeconds(40), Switch: true));
		result.Events.OfType<TaskEventFor>().ShouldHaveSingleItem().ShouldBe(new("222222", new TaskPaused(At.AddSeconds(40))));
		s.Tasks["111111"].State.ShouldBe(TaskState.Running);
		s.Tasks["222222"].State.ShouldBe(TaskState.Paused);
		s.OpenViews.Count(task => task.State == TaskState.Running).ShouldBe(1);
		s.Tracker.TaskIds.Count.ShouldBe(2);
	}

	[Fact]
	public void Switch_flag_without_a_running_task_is_harmless_and_running_target_still_refuses()
	{
		var s = new OpenTasksScenario();
		var created = s.Start("111111", At, switching: true);
		created.Events.OfType<TaskEventFor>().ShouldBeEmpty();
		var refused = s.Decide("111111", new ResumeTask(At.AddSeconds(10), Switch: true));
		refused.Events.ShouldBeEmpty();
		refused.Error!.Code.ShouldBe("invalid_transition");
		s.Send("111111", new PauseTask(At.AddSeconds(20)));
		s.Send("111111", new ResumeTask(At.AddSeconds(30), Switch: true)).Events.OfType<TaskEventFor>().ShouldBeEmpty();
	}

	[Fact]
	public void Explicit_stop_of_a_paused_task_keeps_the_other_task_running()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		s.Start("222222", At.AddSeconds(20), switching: true);
		s.Send("111111", new StopTask(At.AddSeconds(30)));
		s.Tasks["111111"].State.ShouldBe(TaskState.Stopped);
		s.Tasks["222222"].State.ShouldBe(TaskState.Running);
		s.Tracker.TaskIds.ShouldBe(["222222"]);
	}

	[Fact]
	public void Invalid_target_or_switch_time_returns_no_pause_or_tracker_events()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		var negative = s.Decide("222222", new StartTask("222222", s.Seed.Catalogue.ProjectId, null, At.AddSeconds(10), -1m, true));
		negative.Error!.Code.ShouldBe("validation");
		negative.Events.ShouldBeEmpty();
		var early = s.Decide("222222", new StartTask("222222", s.Seed.Catalogue.ProjectId, null, At.AddSeconds(-1), Switch: true));
		early.Error!.Code.ShouldBe("validation");
		early.Events.ShouldBeEmpty();
		s.Tasks["111111"].State.ShouldBe(TaskState.Running);
	}

	[Fact]
	public void Legacy_tracker_events_replace_membership_and_old_documents_load_without_new_legacy_output()
	{
		var tracker = new OpenTask();
		tracker.Evolve(new TaskOpened("111111"));
		tracker.Evolve(new TaskOpened("222222"));
		tracker.Evolve(new OpenTaskSet("333333"));
		tracker.TaskIds.ShouldBe(["333333"]);
		tracker.Evolve(new OpenTaskCleared());
		tracker.TaskIds.ShouldBeEmpty();

		var options = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase
		};

		var old = JsonSerializer.Deserialize<OpenTask>("""{"id":"tracker","taskId":"111111"}""", options)!;
		old.TaskIds.ShouldBe(["111111"]);
		JsonSerializer.Serialize(old, options).ShouldNotContain("\"taskId\":");
		JsonSerializer.Serialize(old, options).ShouldContain("\"taskIds\":[\"111111\"]");
	}

	[Fact]
	public void New_tracker_events_preserve_other_members_and_every_lifecycle_success_serialises()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", At);
		s.Send("111111", new PauseTask(At.AddSeconds(10)));
		s.Send("111111", new ResumeTask(At.AddSeconds(20)));
		s.Send("111111", new StopTask(At.AddSeconds(30)));
		s.Send("111111", new ResumeTask(At.AddSeconds(40)));
		s.Start("222222", At.AddSeconds(50), switching: true);
		s.TrackerEvents.Count.ShouldBe(6);
		s.TrackerEvents.All(e => e is TaskOpened or TaskClosed or TrackerAdvanced).ShouldBeTrue();
		var replay = new OpenTask();
		foreach (var e in s.TrackerEvents) replay.Evolve(e);
		replay.TaskIds.ShouldBe(s.Tracker.TaskIds);

		foreach (var (id, events) in s.TaskEvents)
		{
			var task = new TaskView();
			foreach (var e in events) task.Evolve(e);
			JsonSerializer.Serialize(task).ShouldBe(JsonSerializer.Serialize(s.Tasks[id]));
		}
	}
}
