using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class CorrectionTests
{
	[Fact]
	public void Amendments_reject_overlap_and_provisional_figures_follow_catalogue_changes()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
		s.Send(new ResumeTask(TaskScenario.At.AddSeconds(40)));
		s.Send(new StopTask(TaskScenario.At.AddSeconds(60)));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(999m)));
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));

		s.Figures.RoundedSeconds.ShouldBe(60);
		s.Figures.Currency.ShouldBe("EUR");
		s.Figures.Earnings.ShouldBe(999m / 60m);

		var overlap = s.Decide(new AmendInterval(1, End: TaskScenario.At.AddSeconds(41)));

		overlap.Error!.Code.ShouldBe("validation");
		overlap.Events.ShouldBeEmpty();
		s.Decide(new AmendInterval(2, Start: TaskScenario.At.AddSeconds(19))).Error!.Code.ShouldBe("validation");
		s.Decide(new AmendInterval(2, End: TaskScenario.At.AddSeconds(39))).Error!.Code.ShouldBe("validation");
		s.Decide(new AmendInterval(3, End: TaskScenario.At.AddSeconds(100))).Error!.Code.ShouldBe("not_found");

		s.Send(new AmendInterval(1, End: TaskScenario.At.AddSeconds(40)));
		s.Send(new AmendInterval(2, End: TaskScenario.At.AddSeconds(100)));

		s.Task.RawSeconds.ShouldBe(100);
		s.Figures.RoundedSeconds.ShouldBe(120);
		s.Figures.Earnings.ShouldBe(999m / 30m);
	}

	[Fact]
	public void Backfill_does_not_claim_the_open_task_and_leaves_figures_provisional()
	{
		var s = new TaskScenario();
		s.Start();
		var recorded = new TaskView();

		var result = TaskDecider.Decide(recorded,
			s.Tracker,
			s.Catalogue.State,
			new RecordTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At.AddHours(-1), TaskScenario.At),
			[s.Task]);

		result.IsSuccess.ShouldBeTrue();
		result.Events.Count.ShouldBe(2);

		recorded.Evolve(result.Events.OfType<TaskRecorded>().Single());

		recorded.State.ShouldBe(TaskState.Stopped);
		recorded.RawSeconds.ShouldBe(3600);
		TaskFigures.Resolve(recorded, s.Catalogue.State, TaskScenario.At).Rate.ShouldBe(120m);
		s.Tracker.TaskIds.ShouldContain(s.Task.Id);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Archived_client_or_project_cannot_start_or_backfill(bool client)
	{
		var s = new TaskScenario();

		if (client) s.Catalogue.Send(new ArchiveClient(s.Catalogue.ClientId));
		else s.Catalogue.Send(new ArchiveProject(s.Catalogue.ProjectId));

		s.Decide(new StartTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At)).Error!.Code.ShouldBe("validation");
		s.Decide(new RecordTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At)).Error!.Code.ShouldBe("validation");
	}

	[Fact]
	public void Invalid_timestamps_and_missing_projects_are_refused()
	{
		var s = new TaskScenario();

		s.Decide(new StartTask("BAD", s.Catalogue.ProjectId, null, TaskScenario.At)).Error!.Code.ShouldBe("validation");
		s.Decide(new StartTask("123abc", Guid.CreateVersion7(), null, TaskScenario.At)).Error!.Code.ShouldBe("not_found");

		s.Decide(new RecordTask("123abc", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At.AddSeconds(-1))).Error!.Code
			.ShouldBe("validation");

		s.Start();

		s.Decide(new PauseTask(TaskScenario.At.AddSeconds(-1))).Error!.Code.ShouldBe("validation");
		s.Decide(new StopTask(TaskScenario.At.AddSeconds(-1))).Error!.Code.ShouldBe("validation");
		s.Decide(new AmendInterval(1, End: TaskScenario.At.AddSeconds(10))).Error!.Code.ShouldBe("validation");

		s.Send(new PauseTask(TaskScenario.At.AddSeconds(10)));

		s.Decide(new ResumeTask(TaskScenario.At.AddSeconds(9))).Error!.Code.ShouldBe("validation");
		s.Decide(new StopTask(TaskScenario.At.AddSeconds(9))).Error!.Code.ShouldBe("validation");
	}

	[Fact]
	public void Reassignment_and_ref_clear_resolve_the_destination_project_policy()
	{
		var s = new TaskScenario();
		s.Reach(TaskState.Stopped);
		var project = Guid.CreateVersion7();
		s.Catalogue.Send(new AddProject(project, s.Catalogue.ClientId, "other", Rate: 999m));
		s.Send(new ReassignTask(project));
		s.Send(new ChangeTaskRef(null));

		s.Task.ProjectId.ShouldBe(project);
		s.Task.Ref.ShouldBeNull();
		s.Figures.Rate.ShouldBe(999m);

		s.Send(new VoidTask());

		s.Decide(new AmendInterval(1)).Error!.Code.ShouldBe("invalid_transition");
		s.Decide(new ReassignTask(project)).Error!.Code.ShouldBe("invalid_transition");
		s.Decide(new ChangeTaskRef("new-ref")).Error!.Code.ShouldBe("invalid_transition");
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	public void Empty_amendments_are_validation_refusals_without_events(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var result = s.Decide(new AmendInterval(1));

		result.Error!.Code.ShouldBe("validation");
		result.Error.Message.ShouldBe("Specify an interval start or end to amend.");
		result.Events.ShouldBeEmpty();
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	public void Amendments_apply_in_each_editable_state(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var start = TaskScenario.At.AddSeconds(-1);
		s.Send(new AmendInterval(1, Start: start));

		s.Task.Timeline.Single().Start.ShouldBe(start);
		s.Task.StartedAt.ShouldBe(start);
		s.Task.State.ShouldBe(state);
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	public void Reassignment_applies_in_each_editable_state(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var project = Guid.CreateVersion7();
		s.Catalogue.Send(new AddProject(project, s.Catalogue.ClientId, "other", Rate: 999m));
		s.Send(new ReassignTask(project));

		s.Task.ProjectId.ShouldBe(project);
		s.Task.State.ShouldBe(state);
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	public void Ref_changes_and_clears_apply_in_each_editable_state(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		s.Send(new ChangeTaskRef("new-ref"));

		s.Task.Ref.ShouldBe("new-ref");

		s.Send(new ChangeTaskRef(null));

		s.Task.Ref.ShouldBeNull();
		s.Task.State.ShouldBe(state);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Amending_the_final_interval_updates_the_stop_time_for_stopped_and_recorded_tasks(bool recorded)
	{
		var s = new TaskScenario();

		if (recorded)
		{
			s.Send(new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At.AddSeconds(60)));
		}
		else
		{
			s.Start();
			s.Send(new PauseTask(TaskScenario.At.AddSeconds(20)));
			s.Send(new ResumeTask(TaskScenario.At.AddSeconds(40)));
			s.Send(new StopTask(TaskScenario.At.AddSeconds(60)));
		}

		var end = TaskScenario.At.AddSeconds(120);
		s.Send(new AmendInterval(s.Task.Timeline.Count, End: end));

		s.Task.StoppedAt.ShouldBe(end);
		s.Task.StoppedAt.ShouldBe(s.Task.Timeline[^1].End);
		// #1: amending an earlier boundary still uses the final interval's endpoint.
		s.Send(new AmendInterval(1, Start: TaskScenario.At.AddSeconds(-1)));

		s.Task.StoppedAt.ShouldBe(end);
	}

	[Theory]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Voided)]
	public void Amendments_leave_non_stopped_tasks_stop_times_unchanged(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var before = s.Task.StoppedAt;
		var command = new AmendInterval(1, Start: TaskScenario.At.AddSeconds(-1));

		if (state == TaskState.Voided)
		{
			var result = s.Decide(command);
			result.Error!.Code.ShouldBe("invalid_transition");
			result.Events.ShouldBeEmpty();
		}
		else
		{
			s.Send(command);
		}

		s.Task.StoppedAt.ShouldBe(before);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(29, 0)]
	[InlineData(30, 60)]
	[InlineData(59, 60)]
	[InlineData(60, 60)]
	[InlineData(89, 60)]
	[InlineData(90, 120)]
	public void Rounding_is_half_up_on_the_summed_seconds(long raw, long rounded) => TaskMath.Round(raw).ShouldBe(rounded);
}
