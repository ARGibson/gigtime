using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class SwitchGuardTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Confirmed_switch_refuses_a_different_running_task_without_changing_any_task(bool resume)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var target = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		await app.PostAsync<TaskDto>("/v1/tasks/pause");
		var prompted = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;

		var first = resume
			? await app.PostAsync<TaskDto>($"/v1/tasks/resume?task={target.Id}", status: 409)
			: await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"), 409);

		first.Error!.State!.TaskId.ShouldBe(prompted.Id);
		var current = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project", Switch: true))).Data!;

		var retry = resume
			? await app.PostAsync<TaskDto>($"/v1/tasks/resume?task={target.Id}&switch=true&switch_from={prompted.Id}", status: 409)
			: await app.PostAsync<TaskDto>("/v1/tasks/start",
				new StartTaskRequest("example", "project", Switch: true, SwitchFrom: prompted.Id),
				409);

		retry.Error!.Code.ShouldBe("invalid_transition");
		retry.Error.State!.TaskId.ShouldBe(current.Id);
		var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;
		status.Running!.Id.ShouldBe(current.Id);
		status.Paused.Select(task => task.Id).Order().ShouldBe(new[] { target.Id, prompted.Id }.Order());
		(await app.GetAsync<TasksDto>("/v1/tasks?all=true")).Data!.Tasks.Count.ShouldBe(3);
	}

	[Fact]
	public async Task A_hidden_same_second_pause_does_not_block_amending_added_visible_time()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var task = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		await app.PostAsync<TaskDto>("/v1/tasks/pause");
		app.Clock.Now = app.Clock.Now.AddMinutes(5);
		await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/add", new AddIntervalInput("08:59", "09:01"));

		var amended = (await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/amend",
			new AmendTaskInput(Interval: 2, Start: "08:58"))).Data!;

		amended.Intervals.ShouldHaveSingleItem().Number.ShouldBe(2);
		amended.RawSeconds.ShouldBe(180);

		(await app.PostAsync<TaskDto>($"/v1/tasks/{task.Id}/add", new AddIntervalInput("07:00", "07:00"), 422))
			.Error!.Code.ShouldBe("validation");

		var recorded = (await app.PostAsync<TaskDto>("/v1/tasks/add",
			new RecordTaskInput("example", "project", "07:00", "07:00"))).Data!;

		recorded.State.ShouldBe("stopped");
		recorded.Intervals.ShouldBeEmpty();
	}
}
