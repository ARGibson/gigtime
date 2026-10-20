using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class OpenTaskRaceTests
{
	[Fact]
	public async Task Concurrent_start_and_resume_from_nothing_running_keep_exactly_one_running_task()
	{
		// #36: unlike switching, these writes share no task stream; the tracker must serialize them.
		for (var round = 0; round < 15; round++)
		{
			await using var app = await ServiceHost.StartAsync();
			await app.CreateProjectAsync();
			var paused = (await app.PostAsync<TaskDto>("/v1/tasks/start", new StartTaskRequest("example", "project"))).Data!;
			app.Clock.Now = app.Clock.Now.AddSeconds(1);
			await app.PostAsync<TaskDto>("/v1/tasks/pause");
			app.Clock.Now = app.Clock.Now.AddSeconds(1);

			var responses = await Task.WhenAll(
				app.Host.Scenario(s =>
				{
					s.Post.Json(new StartTaskRequest("example", "project")).ToUrl("/v1/tasks/start");
					s.IgnoreStatusCode();
				}),
				app.Host.Scenario(s =>
				{
					s.Post.Url($"/v1/tasks/resume?task={paused.Id}");
					s.IgnoreStatusCode();
				}));

			var envelopes = await Task.WhenAll(responses.Select(response => response.ReadAsJsonAsync<Envelope<TaskDto>>()));
			envelopes.Count(envelope => envelope.Ok).ShouldBe(1, $"round {round}: exactly one command may start running work");
			envelopes.Single(envelope => !envelope.Ok).Error!.Code.ShouldBeOneOf("conflict", "invalid_transition");
			var tasks = (await app.GetAsync<TasksDto>("/v1/tasks?all=true")).Data!.Tasks;
			tasks.Count(task => task.State == "running").ShouldBe(1, $"round {round}: persisted state");
			var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;
			status.Running!.Id.ShouldBe(tasks.Single(task => task.State == "running").Id);
			status.Paused.Count.ShouldBe(tasks.Count(task => task.State == "paused"));
		}
	}
}
