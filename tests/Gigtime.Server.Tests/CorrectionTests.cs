using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Reporting;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class CorrectionTests
{
	[Fact]
	public async Task Backfill_does_not_touch_open_task_and_leaves_figures_provisional()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var open = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;

		var added = (await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("example", "project", "2026-03-02T08:00:00Z", "2026-03-02T08:01:31Z", "backfill"))).Data!;

		added.State.ShouldBe("stopped");
		added.RawSeconds.ShouldBe(91);
		added.RoundedSeconds.ShouldBe(120);
		added.Submission.ShouldBeNull();
		added.Rate.ShouldBeNull();
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Task!.Id.ShouldBe(open.Id);
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().RawSeconds.ShouldBe(91);
	}

	[Fact]
	public async Task Stopped_amendment_uses_current_rate_and_overlap_rejection_is_atomic()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var task = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		await app.PostAsync<TaskDto>("/v1/tasks/pause");
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		await app.PostAsync<TaskDto>("/v1/tasks/resume");
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		await app.PostAsync<TaskDto>("/v1/tasks/stop");
		await app.PatchAsync<ProjectDto>("/v1/clients/example/projects/project", new ChangeProjectRequest(Rate: "100"));

		var amended = (await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/amend",
			new AmendTaskInput(2, End: "2026-03-02T09:04:31Z", Ref: "fixed"))).Data!;

		amended.RawSeconds.ShouldBe(211);
		amended.RoundedSeconds.ShouldBe(240);
		amended.Submission.ShouldBeNull();
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().EstimatedEarnings!.Rounded.ShouldBe("6.67");
		amended.Ref.ShouldBe("fixed");

		var refused = await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/amend",
			new AmendTaskInput(2, Start: "2026-03-02T09:00:30Z", Ref: "not-applied"),
			422);

		refused.Error!.Code.ShouldBe("validation");
		var unchanged = (await app.GetAsync<TaskDto>($"/v1/tasks/{task.Id}")).Data!;
		unchanged.Ref.ShouldBe("fixed");
		unchanged.RawSeconds.ShouldBe(211);
		(await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/amend", new AmendTaskInput(ClearRef: true))).Data!.Ref.ShouldBeNull();
		(await app.PostAsync<TaskDto>("/v1/tasks/abcdef/void", new VoidTaskRequest(), 404)).Error!.Code.ShouldBe("not_found");
	}

	[Fact]
	public async Task Local_and_clock_times_use_client_timezone_and_reassignment_uses_current_project_rate()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("ny", "New York", "USD", "America/New_York"));
		await app.PostAsync<ProjectDto>("/v1/clients/ny/projects", new AddProjectRequest("one", Rate: "20"));
		await app.PostAsync<ProjectDto>("/v1/clients/ny/projects", new AddProjectRequest("two", Rate: "50"));
		var task = (await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "one", "09:00", "2026-03-02T10:00"))).Data!;
		task.StartedAt.Hour.ShouldBe(14);
		task.StoppedAt!.Value.Hour.ShouldBe(15);
		var changed = (await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/amend", new AmendTaskInput(Client: "ny", Project: "two"))).Data!;
		changed.Project.Key.ShouldBe("two");
		changed.Submission.ShouldBeNull();
		(await app.GetAsync<ReportDto>("/v1/report")).Data!.Totals.ShouldHaveSingleItem().EstimatedEarnings!.Rounded.ShouldBe("50.00");

		(await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "one", "2026-03-08T02:15", "2026-03-08T04:00"), 422))
			.Error!.Code.ShouldBe("validation");

		(await app.PostAsync<TaskDto>("/v1/tasks/add", new RecordTaskInput("ny", "one", "2026-11-01T01:15", "2026-11-01T03:00"), 422))
			.Error!.Code.ShouldBe("validation");
	}
}
