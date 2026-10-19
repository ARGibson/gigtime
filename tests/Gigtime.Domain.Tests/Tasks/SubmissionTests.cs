using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Domain.Tests.Tasks;

public sealed class SubmissionTests
{
	[Fact]
	public void Submission_freezes_current_figures_and_unsubmit_resolves_them_afresh()
	{
		var s = new TaskScenario();
		s.Start();
		s.Send(new StopTask(TaskScenario.At.AddSeconds(90)));
		s.Task.Snapshot.ShouldBeNull();
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(180m)));
		s.Figures.Earnings.ShouldBe(6m);
		var submittedAt = TaskScenario.At.AddMinutes(3);
		s.Send(new SubmitTask(submittedAt));
		s.Task.Submission.ShouldBe(new(submittedAt, 120, "nearest-minute", 180m, "USD"));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(240m)));
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));
		s.Figures.Rate.ShouldBe(180m);
		s.Figures.Currency.ShouldBe("USD");
		s.Figures.Earnings.ShouldBe(6m);
		s.Send(new UnsubmitTask(submittedAt.AddMinutes(1)));
		s.Task.Submission.ShouldBeNull();
		s.Task.State.ShouldBe(TaskState.Stopped);
		s.Figures.Rate.ShouldBe(240m);
		s.Figures.Currency.ShouldBe("EUR");
		s.Send(new AmendInterval(1, End: TaskScenario.At.AddSeconds(150)));
		s.Send(new SubmitTask(submittedAt.AddMinutes(2)));
		s.Task.Submission.ShouldBe(new(submittedAt.AddMinutes(2), 180, "nearest-minute", 240m, "EUR"));
		s.Figures.Earnings.ShouldBe(12m);
	}

	[Theory]
	[InlineData("start")]
	[InlineData("add")]
	[InlineData("amend")]
	public void Own_rate_wins_over_the_project_and_can_be_cleared(string command)
	{
		var s = new TaskScenario();

		if (command == "add")
		{
			s.Send(new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At.AddHours(1), 75m));
		}
		else
		{
			s.Send(new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, command == "start" ? 75m : null));
			if (command == "amend") s.Send(new SetTaskRate(75m));
			s.Send(new StopTask(TaskScenario.At.AddHours(1)));
		}

		s.Task.Rate.ShouldBe(new(75m, "USD"));
		s.TaskEvents.OfType<TaskRateSet>().ShouldHaveSingleItem().ShouldBe(new(75m, "USD"));
		s.Catalogue.Send(new ChangeProject(s.Catalogue.ProjectId, Rate: Change.Set<decimal?>(500m)));
		s.Send(new SubmitTask(TaskScenario.At.AddHours(2)));
		s.Figures.Earnings.ShouldBe(75m);
		s.Send(new UnsubmitTask(TaskScenario.At.AddHours(3)));
		s.Send(new SetTaskRate(null));
		s.Task.Rate.ShouldBeNull();
		s.TaskEvents.OfType<TaskRateSet>().Last().ShouldBe(new(null, null));
		s.Send(new SubmitTask(TaskScenario.At.AddHours(4)));
		s.Figures.Earnings.ShouldBe(500m);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Stop_and_submit_emits_both_task_events_with_the_tracker_clear_in_one_decision(bool paused)
	{
		var s = new TaskScenario();
		s.Start();
		if (paused) s.Send(new PauseTask(TaskScenario.At.AddSeconds(40)));
		var result = s.Decide(new StopTask(TaskScenario.At.AddSeconds(90), Submit: true));
		result.Error.ShouldBeNull();
		result.Events.Count.ShouldBe(3);
		result.Events[0].ShouldBe(new TaskStopped(TaskScenario.At.AddSeconds(90)));
		result.Events[1].ShouldBe(new TaskSubmitted(TaskScenario.At.AddSeconds(90), paused ? 60 : 120, "nearest-minute", 120m, "USD"));
		result.Events[2].ShouldBeOfType<OpenTaskCleared>();
		// The pure decision leaves both input documents untouched.
		s.Task.State.ShouldBe(paused ? TaskState.Paused : TaskState.Running);
		s.Tracker.TaskId.ShouldBe(s.Task.Id);
		s.Send(new StopTask(TaskScenario.At.AddSeconds(90), Submit: true));
		s.Task.State.ShouldBe(TaskState.Submitted);
		s.Tracker.TaskId.ShouldBeNull();
	}

	[Theory]
	[InlineData("start")]
	[InlineData("add")]
	[InlineData("pause")]
	[InlineData("resume")]
	[InlineData("stop")]
	[InlineData("submit")]
	[InlineData("amend")]
	[InlineData("project")]
	[InlineData("ref")]
	[InlineData("rate")]
	[InlineData("void")]
	public void Submitted_tasks_refuse_mutations_with_the_real_unsubmit_hint_and_no_events(string command)
	{
		var s = new TaskScenario();
		s.Reach(TaskState.Submitted);

		TaskCommand mutation = command switch
		{
			"start" => new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At),
			"add" => new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At),
			"pause" => new PauseTask(TaskScenario.At),
			"resume" => new ResumeTask(TaskScenario.At),
			"stop" => new StopTask(TaskScenario.At, true),
			"submit" => new SubmitTask(TaskScenario.At),
			"amend" => new AmendInterval(1, End: TaskScenario.At.AddHours(1)),
			"project" => new ReassignTask(s.Catalogue.ProjectId),
			"ref" => new ChangeTaskRef("new"),
			"rate" => new SetTaskRate(null),
			"void" => new VoidTask(),
			_ => throw new InvalidOperationException(),
		};

		var result = s.Decide(mutation);
		result.Events.ShouldBeEmpty();
		result.Error!.Code.ShouldBe("invalid_transition");
		result.Error.State.ShouldBe("submitted");
		result.Error.Message.ShouldContain("submitted");
		result.Error.Hint.ShouldBe("gig unsubmit 7f3a9c");
		s.Task.State.ShouldBe(TaskState.Submitted);
	}

	[Fact]
	public void Unsubmitted_task_can_be_amended_and_voided()
	{
		var s = new TaskScenario();
		s.Reach(TaskState.Submitted);
		s.Send(new UnsubmitTask(TaskScenario.At.AddMinutes(1)));
		s.Send(new AmendInterval(1, End: TaskScenario.At.AddMinutes(1)));
		s.Send(new ChangeTaskRef("corrected"));
		s.Send(new SetTaskRate(90m));
		s.Send(new VoidTask("duplicate"));
		s.Task.State.ShouldBe(TaskState.Voided);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void Currency_change_refuses_submission_without_stopping_until_own_rate_is_updated(bool onStop, bool clear)
	{
		var s = new TaskScenario();
		s.Send(new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, 75m));
		if (!onStop) s.Send(new StopTask(TaskScenario.At.AddMinutes(1)));
		s.Catalogue.Send(new ChangeClient(s.Catalogue.ClientId, Currency: Change.Set("EUR")));

		TaskCommand command = onStop
			? new StopTask(TaskScenario.At.AddMinutes(1), true)
			: new SubmitTask(TaskScenario.At.AddMinutes(1));

		var result = s.Decide(command);
		result.Error!.Code.ShouldBe("validation");
		result.Events.ShouldBeEmpty();
		s.Task.State.ShouldBe(onStop ? TaskState.Running : TaskState.Stopped);
		s.Send(new SetTaskRate(clear ? null : 80m));
		s.Task.Rate.ShouldBe(clear ? null : new(80m, "EUR"));
		s.Send(command);
		s.Task.Submission!.Currency.ShouldBe("EUR");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Cross_currency_reassignment_requires_an_atomic_rate_change_or_clear(bool clear)
	{
		var s = new TaskScenario();
		s.Send(new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, 75m));
		var client = Guid.CreateVersion7();
		var project = Guid.CreateVersion7();
		s.Catalogue.Send(new AddClient(client, "other", "Other", "EUR"));
		s.Catalogue.Send(new AddProject(project, client, "other", Rate: 200m));
		var refused = s.Decide(new ReassignTask(project));
		refused.Error!.Code.ShouldBe("validation");
		refused.Events.ShouldBeEmpty();
		s.Task.ProjectId.ShouldBe(s.Catalogue.ProjectId);
		s.Send(new ReassignTask(project, Change.Set<decimal?>(clear ? null : 90m)));
		s.Task.ProjectId.ShouldBe(project);
		s.Task.Rate.ShouldBe(clear ? null : new(90m, "EUR"));
		s.Figures.Rate.ShouldBe(clear ? 200m : 90m);
	}

	[Theory]
	[InlineData("start")]
	[InlineData("add")]
	[InlineData("amend")]
	[InlineData("project")]
	public void Negative_own_rates_are_validation_refusals_without_events(string command)
	{
		var s = new TaskScenario();
		if (command is "amend" or "project") s.Start();

		TaskCommand change = command switch
		{
			"start" => new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, -1m),
			"add" => new RecordTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, TaskScenario.At, -1m),
			"project" => new ReassignTask(s.Catalogue.ProjectId, Change.Set<decimal?>(-1m)),
			_ => new SetTaskRate(-1m),
		};

		var result = s.Decide(change);
		result.Error!.Code.ShouldBe("validation");
		result.Events.ShouldBeEmpty();
	}

	[Fact]
	public void New_submission_and_rate_events_replay_to_the_persisted_task_and_tracker()
	{
		var s = new TaskScenario();
		s.Send(new StartTask("7f3a9c", s.Catalogue.ProjectId, null, TaskScenario.At, 60m));
		var running = TaskFigures.Resolve(s.Task, s.Catalogue.State, TaskScenario.At.AddSeconds(90));
		running.RawSeconds.ShouldBe(90);
		running.RoundedSeconds.ShouldBe(120);
		running.Earnings.ShouldBe(2m);
		s.Send(new StopTask(TaskScenario.At.AddSeconds(90), Submit: true));
		s.Send(new UnsubmitTask(TaskScenario.At.AddMinutes(2)));
		s.Send(new SetTaskRate(null));
		s.Send(new SubmitTask(TaskScenario.At.AddMinutes(3)));
		var replay = new TaskView();
		var tracker = new OpenTask();
		foreach (var e in s.TaskEvents) replay.Evolve(e);
		foreach (var e in s.TrackerEvents) tracker.Evolve(e);
		replay.State.ShouldBe(s.Task.State);
		replay.Rate.ShouldBe(s.Task.Rate);
		replay.Submission.ShouldBe(s.Task.Submission);
		replay.Snapshot.ShouldBeNull();
		replay.Timeline.ShouldBe(s.Task.Timeline);
		replay.StoppedAt.ShouldBe(s.Task.StoppedAt);
		TaskFigures.Resolve(replay, s.Catalogue.State, TaskScenario.At.AddHours(1)).ShouldBe(s.Figures);
		tracker.TaskId.ShouldBe(s.Tracker.TaskId);
	}
}
