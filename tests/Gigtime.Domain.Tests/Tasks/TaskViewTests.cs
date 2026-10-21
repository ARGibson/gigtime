using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class TaskViewTests
{
	[Fact]
	public void A_stale_pause_on_an_already_paused_projection_preserves_its_numbered_timeline_and_state()
	{
		var scenario = new TaskScenario();
		scenario.Start();
		scenario.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
		var state = scenario.Task.State;
		state.ShouldBe(TaskState.Paused);
		var timeline = scenario.Task.Timeline.Select(interval => (interval.Number, interval.Start, interval.End)).ToArray();

		// #36: a losing switch can project its pause before the expected-version check rejects it.
		Should.NotThrow(() => scenario.Task.Evolve(new TaskPaused(TaskScenario.At.AddSeconds(30))));

		scenario.Task.Timeline.Select(interval => (interval.Number, interval.Start, interval.End)).ShouldBe(timeline);
		scenario.Task.State.ShouldBe(state);
	}
}
