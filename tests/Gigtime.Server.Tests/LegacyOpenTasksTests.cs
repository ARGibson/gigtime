using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class LegacyOpenTasksTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Old_tracker_events_and_stored_single_task_document_load_without_migration_writes(bool paused)
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		var project = (await app.GetAsync<Gigtime.Contracts.Catalogue.ProjectsDto>("/v1/projects?client=example")).Data!.Projects.Single();

		await using (var session = factory.LightweightSession())
		{
			var events = new List<object>
			{
				new TaskStarted("aaaaaa", project.Id, "legacy", app.Clock.Now.AddMinutes(-2))
			};

			if (paused) events.Add(new TaskPaused(app.Clock.Now.AddMinutes(-1)));
			session.Events.StartStream<TaskView>("aaaaaa", events.ToArray());
			session.Events.Append(OpenTask.StreamId, new OpenTaskSet("aaaaaa"));
			await session.SaveChangesAsync(TestContext.Current.CancellationToken);
		}

		await using (var connection = new SqliteConnection("Data Source=" + Path.Combine(app.DirectoryPath, "data", "gigtime", "gigtime.db")))
		{
			await connection.OpenAsync(TestContext.Current.CancellationToken);
			await using var command = connection.CreateCommand();
			// #36: overwrite only the projection with the actual pre-change JSON shape.
			command.CommandText = "UPDATE fi_doc_opentask SET data = '{\"id\":\"tracker\",\"taskId\":\"aaaaaa\"}' WHERE id = 'tracker'";
			(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
			command.CommandText = "UPDATE fi_doc_taskview SET data = json_remove(data, '$.pausedAt') WHERE id = 'aaaaaa'";
			(await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
		}

		var before = await TrackerCountAsync(app);
		await app.RestartAsync();
		var status = (await app.GetAsync<StatusDto>("/v1/status")).Data!;

		if (paused)
		{
			status.Running.ShouldBeNull();
			status.Paused.ShouldHaveSingleItem().Id.ShouldBe("aaaaaa");
			status.Paused[0].State.ShouldBe("paused");
		}
		else
		{
			status.Running!.Id.ShouldBe("aaaaaa");
			status.Running.State.ShouldBe("running");
			status.Paused.ShouldBeEmpty();
		}

		(await TrackerCountAsync(app)).ShouldBe(before);
		(await app.PostAsync<TaskDto>("/v1/tasks/stop")).Data!.Id.ShouldBe("aaaaaa");
		(await app.GetAsync<StatusDto>("/v1/status")).Data!.Paused.ShouldBeEmpty();
	}

	private static async Task<int> TrackerCountAsync(ServiceHost app)
	{
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var session = factory.QuerySession();

		return (await session.Events.FetchStreamAsync(OpenTask.StreamId, token: TestContext.Current.CancellationToken)).Count;
	}
}
