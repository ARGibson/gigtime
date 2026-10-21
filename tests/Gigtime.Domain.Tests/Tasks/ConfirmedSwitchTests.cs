using System.Text.Json;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class ConfirmedSwitchTests
{
	[Theory]
	[InlineData(false, "matching")]
	[InlineData(false, "different")]
	[InlineData(false, "absent")]
	[InlineData(true, "matching")]
	[InlineData(true, "different")]
	[InlineData(true, "absent")]
	public void Confirmed_start_or_resume_switch_pauses_only_the_expected_running_task(bool resume, string running)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At);
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(10)));
		if (running != "absent") s.Start("222222", TaskScenario.At.AddSeconds(20));
		var target = resume ? "111111" : "333333";
		var expected = running == "different" ? "111111" : "222222";

		TaskCommand command = resume
			? new ResumeTask(TaskScenario.At.AddSeconds(30), Switch: true, SwitchFrom: expected)
			: new StartTask(target, s.Seed.Catalogue.ProjectId, null, TaskScenario.At.AddSeconds(30), Switch: true, SwitchFrom: expected);

		var before = JsonSerializer.Serialize(s.Tasks);
		var tracker = JsonSerializer.Serialize(s.Tracker);
		var result = s.Decide(target, command);
		JsonSerializer.Serialize(s.Tasks).ShouldBe(before);
		JsonSerializer.Serialize(s.Tracker).ShouldBe(tracker);

		if (running == "different")
		{
			result.Events.ShouldBeEmpty();
			result.Error!.Code.ShouldBe("invalid_transition");
			result.Error.TaskId.ShouldBe("222222");
			result.Error.Message.ShouldContain("222222");
			s.Tasks["222222"].State.ShouldBe(TaskState.Running);

			return;
		}

		result.Error.ShouldBeNull();
		result.Events.OfType<TaskEventFor>().Count().ShouldBe(running == "absent" ? 0 : 1);

		if (running == "matching")
			result.Events.OfType<TaskEventFor>().Single().ShouldBe(new("222222", new TaskPaused(TaskScenario.At.AddSeconds(30))));

		s.Send(target, command);
		s.OpenViews.Single(task => task.State == TaskState.Running).Id.ShouldBe(target);
	}
}
