using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class RepeatedRefTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void New_tasks_warn_for_a_nonempty_repeated_ref_in_the_same_project_without_blocking(bool record)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At, reference: "ticket-1");
		s.Send("111111", new PauseTask(TaskScenario.At.AddSeconds(10)));

		var result = record
			? s.Send("222222", new RecordTask("222222", s.Seed.Catalogue.ProjectId, "ticket-1", TaskScenario.At, TaskScenario.At.AddSeconds(30)))
			: s.Start("222222", TaskScenario.At.AddSeconds(20), reference: "ticket-1");

		result.Warnings.ShouldNotBeNull().ShouldHaveSingleItem().Code.ShouldBe("repeated_ref");
		result.Warnings![0].Message.ShouldContain("111111");
		s.Tasks["222222"].State.ShouldBe(record ? TaskState.Stopped : TaskState.Running);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("other")]
	public void Missing_empty_or_different_refs_do_not_warn(string? reference)
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At, reference: "ticket-1");
		s.Start("222222", TaskScenario.At.AddSeconds(10), switching: true, reference: reference).Warnings.ShouldBeNull();
	}

	[Fact]
	public void Stopped_tasks_and_other_projects_do_not_produce_repeated_ref_warnings()
	{
		var s = new OpenTasksScenario();
		s.Start("111111", TaskScenario.At, reference: "ticket-1");
		s.Send("111111", new StopTask(TaskScenario.At.AddSeconds(10)));
		s.Start("222222", TaskScenario.At.AddSeconds(20), reference: "ticket-1").Warnings.ShouldBeNull();
		var project = Guid.CreateVersion7();
		s.Seed.Catalogue.Send(new AddProject(project, s.Seed.Catalogue.ClientId, "other"));
		s.Start("333333", TaskScenario.At.AddSeconds(30), switching: true, reference: "ticket-1", project: project).Warnings.ShouldBeNull();
	}
}
