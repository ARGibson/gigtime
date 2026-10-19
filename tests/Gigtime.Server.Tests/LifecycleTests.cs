using Gigtime.Contracts;
using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class LifecycleTests
{
	[Fact]
	public async Task Lifecycle_uses_the_registered_clock_and_keeps_submitted_totals_after_catalogue_changes()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Task.ShouldBeNull();
		var started = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", "reference"))).Data!;
		started.StartedAt.ShouldBe(app.Clock.Now);
		started.State.ShouldBe("running");
		app.Clock.Now = app.Clock.Now.AddSeconds(40);
		var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!.Task!;
		status.RawSeconds.ShouldBe(40);
		(await app.PostAsync<TaskDto>("/v1/tasks/pause")).Data!.State.ShouldBe("paused");
		(await app.PostAsync<TaskDto>("/v1/tasks/pause", status: 409)).Error!.Code.ShouldBe("invalid_transition");
		app.Clock.Now = app.Clock.Now.AddMinutes(2);
		(await app.PostAsync<TaskDto>("/v1/tasks/resume")).Data!.State.ShouldBe("running");
		(await app.PostAsync<TaskDto>("/v1/tasks/resume", status: 409)).Error!.Code.ShouldBe("invalid_transition");
		app.Clock.Now = app.Clock.Now.AddSeconds(50);
		var stopped = (await app.PostAsync<TaskDto>("/v1/tasks/stop")).Data!;
		stopped.RawSeconds.ShouldBe(90);
		stopped.RoundedSeconds.ShouldBe(120);
		stopped.Submission.ShouldBeNull();
		stopped.Rate.ShouldBeNull();
		stopped.Intervals.Select(i => i.Number).ShouldBe([1, 2]);
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Task.ShouldBeNull();
		var submitted = (await app.PostAsync<TaskDto>($"/v1/tasks/{started.Id}/submit")).Data!;
		submitted.Submission!.Rate!.Amount.ShouldBe("12.50");
		await app.PatchAsync<ProjectDto>("/v1/clients/example/projects/project", new ChangeProjectRequest(Rate: "99"));
		await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Currency: "USD"));
		var read = (await app.GetAsync<TaskDto>($"/v1/tasks/{started.Id}")).Data!;
		read.RawSeconds.ShouldBe(stopped.RawSeconds);
		read.RoundedSeconds.ShouldBe(stopped.RoundedSeconds);
		read.Submission.ShouldBe(submitted.Submission);
		var tasks = (await app.GetAsync<TasksDto>("/v1/tasks?last=1&client=example&project=project&all=true")).Data!.Tasks;
		tasks.ShouldHaveSingleItem().Id.ShouldBe(started.Id);
	}

	[Fact]
	public async Task Missing_and_archived_resources_are_refused_before_events_are_appended()
	{
		await using var app = await ServiceHost.StartAsync();
		(await app.GetAsync<TaskDto>("/v1/tasks/aaaaaa", 404)).Error!.Code.ShouldBe("not_found");

		foreach (var verb in new[] { "pause", "resume", "stop" })
			(await app.PostAsync<TaskDto>($"/v1/tasks/{verb}", status: 404)).Error!.Code.ShouldBe("not_found");

		(await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("absent", "absent"), 404)).Error!.Code.ShouldBe("not_found");
		await app.CreateProjectAsync();
		await app.PostAsync<AcknowledgementDto>("/v1/clients/example/projects/project/archive");
		(await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"), 422)).Error!.Code.ShouldBe("validation");
		(await app.GetAsync<TasksDto>("/v1/tasks")).Data!.Tasks.ShouldBeEmpty();
	}

	[Fact]
	public async Task Start_while_a_task_is_open_is_an_invalid_transition()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"));

		(await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"), 409))
			.Error!.Code.ShouldBe("invalid_transition");

		app.Clock.Now = app.Clock.Now.AddSeconds(-1);
		(await app.PostAsync<TaskDto>("/v1/tasks/stop", status: 422)).Error!.Code.ShouldBe("validation");
	}

	[Theory]
	[InlineData("last=bad", "validation", 422)]
	[InlineData("last=0", "validation", 422)]
	[InlineData("last=-1", "validation", 422)]
	[InlineData("all=bad", "validation", 422)]
	[InlineData("client=absent", "not_found", 404)]
	[InlineData("project=absent", "not_found", 404)]
	public async Task Task_filters_return_error_envelopes(string query, string code, int status)
	{
		await using var app = await ServiceHost.StartAsync();
		(await app.GetAsync<TasksDto>($"/v1/tasks?{query}", status)).Error!.Code.ShouldBe(code);
	}

	[Fact]
	public async Task Log_is_newest_first_defaults_to_ten_and_filters_before_limiting()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		await app.PostAsync<ProjectDto>("/v1/clients/example/projects", new AddProjectRequest("other"));
		var ids = new List<string>();

		for (var i = 0; i < 12; i++)
		{
			var project = i % 2 == 0 ? "project" : "other";
			ids.Add((await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", project))).Data!.Id);
			app.Clock.Now = app.Clock.Now.AddSeconds(10);
			await app.PostAsync<TaskDto>("/v1/tasks/stop");
			app.Clock.Now = app.Clock.Now.AddSeconds(1);
		}

		var recent = await app.GetAsync<TasksDto>("/v1/tasks");
		recent.Command.ShouldBe("log");
		recent.Data!.Tasks.Select(t => t.Id).ShouldBe(ids.AsEnumerable().Reverse().Take(10));
		var filtered = (await app.GetAsync<TasksDto>("/v1/tasks?client=example&project=project&last=2")).Data!.Tasks;
		filtered.Select(t => t.Id).ShouldBe([ids[10], ids[8]]);
	}

	[Fact]
	public async Task Client_and_project_query_parameters_bind_independently()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		await app.PostAsync<ClientDto>("/v1/clients", new AddClientRequest("second", "Second", "GBP"));
		await app.PostAsync<ProjectDto>("/v1/clients/second/projects", new AddProjectRequest("project"));
		await app.PostAsync<ProjectDto>("/v1/clients/second/projects", new AddProjectRequest("other"));
		var ids = new List<string>();

		foreach (var (client, project) in new[] { ("example", "project"), ("second", "project"), ("second", "other") })
		{
			ids.Add((await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest(client, project))).Data!.Id);
			app.Clock.Now = app.Clock.Now.AddSeconds(10);
			await app.PostAsync<TaskDto>("/v1/tasks/stop");
			app.Clock.Now = app.Clock.Now.AddSeconds(1);
		}

		(await app.GetAsync<TasksDto>("/v1/tasks?client=example&project=project&last=1&all=true"))
			.Data!
			.Tasks.ShouldHaveSingleItem()
			.Id.ShouldBe(ids[0]);

		(await app.GetAsync<TasksDto>("/v1/tasks?client=second&project=project&last=1"))
			.Data!
			.Tasks.ShouldHaveSingleItem()
			.Id.ShouldBe(ids[1]);

		(await app.GetAsync<TasksDto>("/v1/tasks?client=example&project=other", 404)).Error!.Code.ShouldBe("not_found");
	}
}
