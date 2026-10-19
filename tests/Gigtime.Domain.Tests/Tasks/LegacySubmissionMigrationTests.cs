using System.Text.Json;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class LegacySubmissionMigrationTests
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void Legacy_stop_and_backfill_freeze_the_original_figures_at_the_original_stop(bool recorded, bool withoutRate)
	{
		var s = new TaskScenario();
		var task = new TaskView();
		var stop = TaskScenario.At.AddSeconds(90);
		decimal? rate = withoutRate ? null : 120m;

		if (recorded)
		{
			task.Evolve(new TaskRecorded("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, stop, "nearest-minute", rate, "USD"));
		}
		else
		{
			task.Evolve(new TaskStarted("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At));
			task.Evolve(new TaskStopped(stop, "nearest-minute", rate, "USD"));
		}

		var migrated = LegacySubmissionMigration.Decide(task);
		migrated.ShouldBe(new(stop, 120, "nearest-minute", rate, "USD"));
		// Planning a migration is pure; only replaying the returned event changes the task.
		task.State.ShouldBe(TaskState.Stopped);
		task.Submission.ShouldBeNull();
		task.Evolve(migrated!);
		task.State.ShouldBe(TaskState.Submitted);
		task.Submission.ShouldBe(new(stop, 120, "nearest-minute", rate, "USD"));
		LegacySubmissionMigration.Decide(task).ShouldBeNull();
		TaskFigures.Resolve(task, s.Catalogue.State, stop).Earnings.ShouldBe(withoutRate ? null : 4m);
	}

	[Theory]
	[InlineData(TaskState.Missing)]
	[InlineData(TaskState.Running)]
	[InlineData(TaskState.Paused)]
	[InlineData(TaskState.Stopped)]
	[InlineData(TaskState.Submitted)]
	[InlineData(TaskState.Voided)]
	public void New_tasks_and_open_or_voided_legacy_tasks_are_not_migrated(TaskState state)
	{
		var s = new TaskScenario();
		s.Reach(state);
		var before = s.Task.State;
		var submission = s.Task.Submission;
		LegacySubmissionMigration.Decide(s.Task).ShouldBeNull();

		if (state == TaskState.Voided)
		{
			// A voided legacy stream still has its old snapshot, but stays voided.
			var legacy = new TaskView();
			legacy.Evolve(new TaskStarted("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At));
			legacy.Evolve(new TaskStopped(TaskScenario.At.AddSeconds(90), "nearest-minute", 120m, "USD"));
			legacy.Evolve(new TaskVoided());
			LegacySubmissionMigration.Decide(legacy).ShouldBeNull();
			legacy.State.ShouldBe(TaskState.Voided);
		}

		s.Task.State.ShouldBe(before);
		s.Task.Submission.ShouldBe(submission);
	}

	[Fact]
	public void Legacy_event_fields_deserialize_while_new_events_omit_the_legacy_snapshot_values()
	{
		var legacy = JsonSerializer.Deserialize<TaskStopped>(
			"""{"At":"2026-03-02T09:01:30Z","Rounding":"nearest-minute","Rate":120,"Currency":"USD"}""")!;

		legacy.Rounding.ShouldBe("nearest-minute");
		legacy.Rate.ShouldBe(120m);
		legacy.Currency.ShouldBe("USD");
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(TaskScenario.At.AddSeconds(90)));
		var stopped = s.TaskEvents.OfType<TaskStopped>().ShouldHaveSingleItem();
		stopped.Rounding.ShouldBeNull();
		stopped.Rate.ShouldBeNull();
		stopped.Currency.ShouldBeNull();
		LegacySubmissionMigration.Decide(s.Task).ShouldBeNull();
	}

	[Fact]
	public void Provisional_figures_ignore_legacy_snapshots_until_the_migration_event_is_applied()
	{
		var s = new TaskScenario();
		var task = new TaskView();

		task.Evolve(new TaskRecorded("7f3a9c",
			s.Catalogue.ProjectId,
			null,
			TaskScenario.At,
			TaskScenario.At.AddHours(1),
			"nearest-minute",
			5m,
			"EUR"));

		var provisional = TaskFigures.Resolve(task, s.Catalogue.State, TaskScenario.At.AddHours(2));
		provisional.Rate.ShouldBe(120m);
		provisional.Currency.ShouldBe("USD");
		task.Evolve(LegacySubmissionMigration.Decide(task)!);
		var submitted = TaskFigures.Resolve(task, s.Catalogue.State, TaskScenario.At.AddHours(2));
		submitted.Rate.ShouldBe(5m);
		submitted.Currency.ShouldBe("EUR");
	}
}
