using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class TaskTargetsTests
{
	[Theory]
	[InlineData("pause")]
	[InlineData("resume")]
	[InlineData("stop")]
	public void No_eligible_task_is_an_invalid_transition(string command)
	{
		var result = TaskTargets.Choose(command, new(), []);
		result.TaskId.ShouldBeNull();
		result.Error!.Code.ShouldBe("invalid_transition");
		result.Error.Command.ShouldBe(command);
	}

	[Theory]
	[InlineData("pause")]
	[InlineData("stop")]
	public void Running_task_takes_precedence_over_paused_tasks(string command)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Start("222222", TaskScenario.At.AddSeconds(10), switching: true);
		var choice = TaskTargets.Choose(command, s.Tracker, s.OpenViews);
		choice.TaskId.ShouldBe("222222");
		choice.Error.ShouldBeNull();
		var resume = TaskTargets.Choose("resume", s.Tracker, s.OpenViews);
		resume.Error!.Code.ShouldBe("invalid_transition");
		resume.Error.TaskId.ShouldBe("222222");
	}

	[Theory]
	[InlineData("resume")]
	[InlineData("stop")]
	public void One_paused_task_is_selected_and_two_are_ambiguous_in_latest_pause_order(string command)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(10)));
		TaskTargets.Choose(command, s.Tracker, s.OpenViews).TaskId.ShouldBe("111111");
		s.Start("222222", TaskScenario.At.AddSeconds(20));
		s.Send("222222", new PauseTask(TaskScenario.At.AddSeconds(30)));
		var choice = TaskTargets.Choose(command, s.Tracker, s.OpenViews);
		choice.TaskId.ShouldBeNull();
		choice.Error!.Code.ShouldBe("invalid_transition");
		choice.Error.Tasks.ShouldBe(["222222", "111111"]);
		TaskTargets.Choose("pause", s.Tracker, s.OpenViews).Error!.Code.ShouldBe("invalid_transition");
	}

	[Fact]
	public void Pause_order_uses_latest_pause_instant_with_a_legacy_interval_fallback()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(10)));
		s.Start("222222", TaskScenario.At.AddSeconds(20));
		s.Send("222222", new PauseTask(TaskScenario.At.AddSeconds(30)));
		// #36: a paused document written before PausedAt existed falls back to its final interval end.
		s.Tasks["222222"].PausedAt = null;
		TaskTargets.Paused(s.OpenViews).Select(task => task.Id).ShouldBe(["222222", "111111"]);
		s.Send("111111", new ResumeTask(TaskScenario.At.AddSeconds(40)));
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(50)));
		TaskTargets.Paused(s.OpenViews).Select(task => task.Id).ShouldBe(["111111", "222222"]);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Editing_a_legacy_paused_timeline_preserves_its_actual_pause_order(bool amend)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(10)));
		s.Start("222222", TaskScenario.At.AddSeconds(20));
		s.Send("222222", new PauseTask(TaskScenario.At.AddSeconds(30)));
		s.Tasks["111111"].PausedAt = null;

		TaskCommand change = amend
			? new AmendInterval(1, End: TaskScenario.At.AddSeconds(50))
			: new AddInterval(TaskScenario.At.AddSeconds(40), TaskScenario.At.AddSeconds(50));

		s.Send("111111", change);
		s.Tasks["111111"].Timeline[^1].End.ShouldBe(TaskScenario.At.AddSeconds(50));
		s.Tasks["111111"].PausedAt.ShouldBe(TaskScenario.At.AddSeconds(10));
		TaskTargets.Paused(s.OpenViews).Select(task => task.Id).ShouldBe(["222222", "111111"]);
		TaskTargets.Choose("resume", s.Tracker, s.OpenViews).Error!.Tasks.ShouldBe(["222222", "111111"]);
	}

	[Fact]
	public void Membership_is_the_authority_for_target_choice()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Send("111111", new StopTask(TaskScenario.At.AddSeconds(10)));
		TaskTargets.Choose("stop", s.Tracker, s.Tasks.Values.ToArray()).Error!.Code.ShouldBe("invalid_transition");
	}
}
