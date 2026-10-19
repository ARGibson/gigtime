using System.Text.Json;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class SubmissionTests
{
	[Fact]
	public async Task Report_keeps_time_but_omits_earnings_when_own_rate_currency_is_stale()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Rate: "40"))).Data!.Id;
		app.Clock.Now = app.Clock.Now.AddSeconds(90);
		await app.PostAsync<TaskDto>("/v1/tasks/stop");
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Unsubmitted.Single().Amount.ShouldNotBeNull();
		await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Currency: "EUR"));

		var report = (await app.GetAsync<ReportDto>("/v1/report")).Data!.Unsubmitted.Single();
		report.RawSeconds.ShouldBe(90);
		report.RoundedSeconds.ShouldBe(120);
		report.Amount.ShouldBeNull();
		(await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit", status: 422)).Error!.Code.ShouldBe("validation");
	}

	[Fact]
	public async Task Provisional_figures_follow_catalogue_until_submission_then_stay_fixed_and_can_be_revised_after_unsubmit()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		app.Clock.Now = app.Clock.Now.AddMinutes(30);
		var stopped = (await app.PostAsync<TaskDto>("/v1/tasks/stop")).Data!;
		stopped.State.ShouldBe("stopped");
		stopped.Submission.ShouldBeNull();
		stopped.Rate.ShouldBeNull();
		await app.PatchAsync<ProjectDto>("/v1/clients/example/projects/project", new ChangeProjectRequest(Rate: "20"));
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Unsubmitted.Single().Amount!.Rounded.ShouldBe("10.00");
		var submitted = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit")).Data!;
		submitted.State.ShouldBe("submitted");
		submitted.Submission!.SubmittedAt.ShouldBe(app.Clock.Now);
		submitted.Submission.Rate!.Amount.ShouldBe("20");
		submitted.Submission.Amount!.Rounded.ShouldBe("10.00");
		await app.PatchAsync<ProjectDto>("/v1/clients/example/projects/project", new ChangeProjectRequest(Rate: "99"));
		(await app.GetAsync<TaskDto>($"/v1/tasks/{id}")).Data!.Submission.ShouldBe(submitted.Submission);
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.Single().Amount!.Rounded.ShouldBe("10.00");
		(await app.PostAsync<TaskDto>($"/v1/tasks/{id}/unsubmit")).Data!.Submission.ShouldBeNull();
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(Rate: "40"));
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		var resubmitted = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit")).Data!;
		resubmitted.Submission!.Amount!.Rounded.ShouldBe("20.00");
		resubmitted.Submission.SubmittedAt.ShouldBe(app.Clock.Now);
		await AssertReplayAsync(app, id);
		var export = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		var text = await export.ReadAsTextAsync();
		text.ShouldContain("task_submitted");
		text.ShouldContain("task_unsubmitted");
		text.ShouldContain("task_rate_set");
	}

	[Theory]
	[InlineData("start")]
	[InlineData("add")]
	[InlineData("amend")]
	public async Task Own_rate_from_each_entry_point_wins_over_project_at_submission(string command)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		string id;

		if (command == "add")
		{
			id = (await app.PostAsync<TaskDto>("/v1/tasks/add",
				new RecordTaskInput("example",
					"project",
					"2026-03-02T09:00:00Z",
					"2026-03-02T09:30:00Z",
					Rate: "40"))).Data!.Id;
		}
		else
		{
			id = (await app.PostAsync<TaskDto>("/v1/tasks/start",
				new StartTaskRequest("example", "project", Rate: command == "start" ? "40" : null))).Data!.Id;

			app.Clock.Now = app.Clock.Now.AddMinutes(30);
			await app.PostAsync<TaskDto>("/v1/tasks/stop");
			if (command == "amend") await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(Rate: "40"));
		}

		var result = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit")).Data!;
		result.Rate!.Amount.ShouldBe("40");
		result.Rate.Currency.ShouldBe("GBP");
		result.Submission!.Rate.ShouldBe(result.Rate);
		result.Submission.Amount!.Rounded.ShouldBe("20.00");
	}

	[Fact]
	public async Task Submitted_amend_and_void_refuse_before_append_with_real_unsubmit_hint_then_work_after_unsubmit()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		await app.PostAsync<TaskDto>("/v1/tasks/stop?submit=true");
		var before = await ExportAsync(app);

		foreach (var input in new[]
						{
							new AmendTaskInput(Ref: "changed"), new AmendTaskInput(Rate: "999"), new AmendTaskInput(ClearRate: true),
							new AmendTaskInput(Interval: 1, End: "2026-03-02T09:01:00Z"), new AmendTaskInput(Client: "missing", Project: "missing")
						})
		{
			var refusal = await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", input, 409);
			refusal.Error!.Code.ShouldBe("invalid_transition");
			refusal.Error.Hint.ShouldBe($"gig unsubmit {id}");
			refusal.Error.State!.State.ShouldBe("submitted");
		}

		var refusedVoid = await app.PostAsync<TaskDto>($"/v1/tasks/{id}/void", new VoidTaskRequest(), 409);
		refusedVoid.Error!.Hint.ShouldBe($"gig unsubmit {id}");
		(await ExportAsync(app)).ShouldBe(before);
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/unsubmit");
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(Ref: "changed"));
		(await app.PostAsync<TaskDto>($"/v1/tasks/{id}/void", new VoidTaskRequest())).Data!.State.ShouldBe("voided");
	}

	[Fact]
	public async Task Currency_mismatch_refuses_submission_and_moves_until_rate_is_updated_or_cleared()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("second", "Second", "USD"));
		await app.PostAsync<ProjectDto>("/v1/clients/second/projects", new AddProjectRequest("other", Rate: "30"));
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Rate: "40"))).Data!.Id;
		await app.PostAsync<TaskDto>("/v1/tasks/stop");

		(await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(Client: "second", Project: "other"), 422))
			.Error!.Code.ShouldBe("validation");

		var moved = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend",
			new AmendTaskInput(Client: "second", Project: "other", Rate: "50"))).Data!;

		moved.Rate!.Currency.ShouldBe("USD");
		await app.PatchAsync<ClientDto>("/v1/clients/second", new ChangeClientRequest(Currency: "EUR"));
		(await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit", status: 422)).Error!.Code.ShouldBe("validation");
		await app.PostAsync<TaskDto>($"/v1/tasks/{id}/amend", new AmendTaskInput(ClearRate: true));
		var final = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit")).Data!;
		final.Rate.ShouldBeNull();
		final.Submission!.Currency.ShouldBe("EUR");
		final.Submission.Rate!.Amount.ShouldBe("30");
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Stop_submit_appends_stop_submission_and_tracker_change_together(bool paused)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		app.Clock.Now = app.Clock.Now.AddSeconds(40);
		if (paused) await app.PostAsync<TaskDto>("/v1/tasks/pause");
		var result = (await app.PostAsync<TaskDto>("/v1/tasks/stop?submit=true", correlationId: "atomic-submit")).Data!;
		result.State.ShouldBe("submitted");
		result.Submission!.RoundedSeconds.ShouldBe(60);
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Task.ShouldBeNull();
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var session = factory.QuerySession();
		var events = await session.Events.FetchStreamAsync(id, token: TestContext.Current.CancellationToken);
		events.TakeLast(2).Select(e => e.Data.GetType()).ShouldBe([typeof(TaskStopped), typeof(TaskSubmitted)]);
		events.TakeLast(2).ShouldAllBe(e => e.CorrelationId == "atomic-submit");
		await AssertReplayAsync(app, id);
	}

	[Fact]
	public async Task Submit_refuses_open_tasks_and_unknown_ids_and_completion_filters_before_limiting()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		(await app.PostAsync<TaskDto>("/v1/tasks/missing/submit", status: 404)).Error!.Code.ShouldBe("not_found");
		(await app.PostAsync<TaskDto>("/v1/tasks/missing/unsubmit", status: 404)).Error!.Code.ShouldBe("not_found");
		var id = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;

		foreach (var pause in new[] { false, true })
		{
			if (pause) await app.PostAsync<TaskDto>("/v1/tasks/pause");
			var error = (await app.PostAsync<TaskDto>($"/v1/tasks/{id}/submit", status: 409)).Error!;
			error.Hint.ShouldBe("gig stop --submit");
		}

		await app.PostAsync<TaskDto>("/v1/tasks/stop?submit=true");
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		var newer = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!.Id;
		await app.PostAsync<TaskDto>("/v1/tasks/stop");
		(await app.GetAsync<TasksDto>("/v1/tasks?state=submitted&last=1")).Data!.Tasks.ShouldHaveSingleItem().Id.ShouldBe(id);
		(await app.GetAsync<TasksDto>("/v1/tasks?state=stopped&last=1")).Data!.Tasks.ShouldHaveSingleItem().Id.ShouldBe(newer);
		(await app.GetAsync<TasksDto>("/v1/tasks?state=unknown", 422)).Error!.Code.ShouldBe("validation");
	}

	private static async Task<string> ExportAsync(ServiceHost app)
	{
		var result = await app.Host.Scenario(s => s.Get.Url("/v1/export"));

		return await result.ReadAsTextAsync();
	}

	private static async Task AssertReplayAsync(ServiceHost app, string id)
	{
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var session = factory.QuerySession();
		var replay = new TaskView();
		foreach (var e in await session.Events.FetchStreamAsync(id, token: TestContext.Current.CancellationToken)) replay.Evolve(e.Data);

		JsonSerializer
			.Serialize(await session.LoadAsync<TaskView>(id, TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(replay));
	}
}
