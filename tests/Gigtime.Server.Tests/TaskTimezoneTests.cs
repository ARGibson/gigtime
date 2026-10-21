using Gigtime.Contracts.Catalogue;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class TaskTimezoneTests
{
	[Fact]
	public async Task Task_responses_carry_effective_client_timezone_and_preserve_UTC_instants()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.PostAsync<DefaultsDto>("/v1/defaults", new SetDefaultsRequest(Timezone: "America/New_York"));
		await app.CreateProjectAsync();
		var task = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
		task.Timezone.ShouldBe("America/New_York");
		task.StartedAt.ShouldBe(app.Clock.Now);
		task.StartedAt.Offset.ShouldBe(TimeSpan.Zero);
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Running!.Timezone.ShouldBe(task.Timezone);
		(await app.GetAsync<TasksDto>("/v1/tasks")).Data!.Tasks.Single().Timezone.ShouldBe(task.Timezone);
		await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(Timezone: "Europe/London"));
		var read = (await app.GetAsync<TaskDto>("/v1/tasks/" + task.Id)).Data!;
		read.Timezone.ShouldBe("Europe/London");
		read.StartedAt.ShouldBe(task.StartedAt);
		read.Intervals.ShouldBe(task.Intervals);
		await app.PatchAsync<ClientDto>("/v1/clients/example", new ChangeClientRequest(ClearTimezone: true));
		(await app.PostAsync<TaskDto>("/v1/tasks/pause")).Data!.Timezone.ShouldBe("America/New_York");
	}
}
