using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class OpenTasksTests
{
	[Fact]
	public async Task Switch_keeps_paused_work_and_bare_stop_selects_running_then_the_only_paused_task()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var a = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		var b = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Switch: true))).Data!;
		var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;
		status.Running!.Id.ShouldBe(b.Id);
		status.Paused.ShouldHaveSingleItem().Id.ShouldBe(a.Id);
		(await app.GetAsync<TaskDto>($"/v1/tasks/{a.Id}")).Data!.Intervals.ShouldHaveSingleItem().End.ShouldBe(app.Clock.Now);
		(await app.PostAsync<TaskDto>("/v1/tasks/stop")).Data!.Id.ShouldBe(b.Id);
		(await app.PostAsync<TaskDto>("/v1/tasks/stop")).Data!.Id.ShouldBe(a.Id);
		status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;
		status.Running.ShouldBeNull();
		status.Paused.ShouldBeEmpty();
	}

	[Fact]
	public async Task Ambiguous_targets_list_recently_paused_first_and_explicit_targets_act_on_the_named_task()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var a = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		var b = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Switch: true))).Data!;
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		await app.PostAsync<TaskDto>("/v1/tasks/pause");

		foreach (var command in new[] { "resume", "stop" })
		{
			var refused = await app.PostAsync<TaskDto>("/v1/tasks/" + command, status: 409);
			refused.Error!.Code.ShouldBe("invalid_transition");
			refused.Error.State!.Tasks.ShouldBe([b.Id, a.Id]);
		}

		(await app.PostAsync<TaskDto>($"/v1/tasks/resume?task={a.Id}")).Data!.Id.ShouldBe(a.Id);
		var blocked = await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"), 409);
		blocked.Error!.State!.TaskId.ShouldBe(a.Id);
		(await app.PostAsync<TaskDto>($"/v1/tasks/stop?task={b.Id}")).Data!.State.ShouldBe("stopped");
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Running!.Id.ShouldBe(a.Id);
		(await app.PostAsync<TaskDto>($"/v1/tasks/pause?task={b.Id}", status: 409)).Error!.Code.ShouldBe("invalid_transition");
	}

	[Fact]
	public async Task Reopen_add_interval_warning_and_export_keep_one_task_history()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var a = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", "same"))).Data!;
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		await app.PostAsync<TaskDto>("/v1/tasks/pause");

		var repeat = await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("example", "project", "07:00", "07:01", "same"));

		repeat.Ok.ShouldBeTrue();
		repeat.Warnings.ShouldHaveSingleItem().Code.ShouldBe("repeated_ref");
		await app.PostAsync<TaskDto>("/v1/tasks/stop");
		var added = (await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/add", new AddIntervalInput("08:00", "08:01"))).Data!;
		added.Intervals.Select(i => i.Number).ShouldBe([2, 1]);
		await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/add", new AddIntervalInput("08:00", "08:01"), 422);
		await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/add", new AddIntervalInput("06:00", "06:01", Ref: "no"), 422);
		await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/submit");
		var refused = await app.PostAsync<TaskDto>($"/v1/tasks/resume?task={a.Id}", status: 409);
		refused.Error!.Code.ShouldBe("invalid_transition");
		refused.Error.Hint.ShouldBe("gig unsubmit " + a.Id);
		await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/add", new AddIntervalInput("06:00", "06:01"), 409);
		await app.PostAsync<TaskDto>($"/v1/tasks/{a.Id}/unsubmit");
		app.Clock.Now = app.Clock.Now.AddMinutes(1);
		var reopened = (await app.PostAsync<TaskDto>($"/v1/tasks/resume?task={a.Id}")).Data!;
		reopened.Intervals.Select(i => i.Number).ShouldBe([2, 1, 3]);
		reopened.StoppedAt.ShouldBeNull();
		var export = await app.Host.Scenario(s => s.Get.Url("/v1/export"));
		var text = await export.ReadAsTextAsync();
		text.ShouldContain("task_reopened");
		text.ShouldContain("interval_added");
		text.ShouldContain("task_opened");
		await AssertReplayAsync(app, a.Id);
	}

	[Fact]
	public async Task Concurrent_switch_commands_never_leave_two_tasks_running()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var a = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		app.Clock.Now = app.Clock.Now.AddSeconds(1);
		await app.PostAsync<TaskDto>("/v1/tasks/pause");
		await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"));
		app.Clock.Now = app.Clock.Now.AddSeconds(1);
		var host = app.Host;

		var responses = await Task.WhenAll(
			host.Scenario(s =>
			{
				s.Post.Json(new StartTaskRequest("example", "project", Switch: true)).ToUrl("/v1/tasks/start");
				s.IgnoreStatusCode();
			}),
			host.Scenario(s =>
			{
				s.Post.Url($"/v1/tasks/resume?task={a.Id}&switch=true");
				s.IgnoreStatusCode();
			}));

		var envelopes = await Task.WhenAll(responses.Select(r => r.ReadAsJsonAsync<Envelope<TaskDto>>()));
		envelopes.Count(e => e.Ok).ShouldBeGreaterThanOrEqualTo(1);
		foreach (var error in envelopes.Where(e => !e.Ok)) error.Error!.Code.ShouldBeOneOf("conflict", "invalid_transition");
		var tasks = (await app.GetAsync<TasksDto>("/v1/tasks?all=true")).Data!.Tasks;
		tasks.Count(t => t.State == "running").ShouldBe(1);
		var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;
		status.Running!.Id.ShouldBe(tasks.Single(t => t.State == "running").Id);
		status.Paused.Count.ShouldBe(tasks.Count(t => t.State == "paused"));
		foreach (var task in tasks) await AssertReplayAsync(app, task.Id);
	}

	private static async Task AssertReplayAsync(ServiceHost app, string id)
	{
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var session = factory.QuerySession();
		var replay = new TaskView();
		foreach (var e in await session.Events.FetchStreamAsync(id, token: TestContext.Current.CancellationToken)) replay.Evolve(e.Data);
		var stored = (await session.LoadAsync<TaskView>(id, TestContext.Current.CancellationToken))!;
		System.Text.Json.JsonSerializer.Serialize(stored).ShouldBe(System.Text.Json.JsonSerializer.Serialize(replay));
		var tracker = new OpenTask();

		foreach (var e in await session.Events.FetchStreamAsync(OpenTask.StreamId, token: TestContext.Current.CancellationToken))
			tracker.Evolve(e.Data);

		System
			.Text.Json.JsonSerializer.Serialize(await session.LoadAsync<OpenTask>(OpenTask.StreamId, TestContext.Current.CancellationToken))
			.ShouldBe(System.Text.Json.JsonSerializer.Serialize(tracker));
	}
}
