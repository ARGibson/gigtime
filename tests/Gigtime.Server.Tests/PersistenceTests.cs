using System.Text.Json;
using Gigtime.Contracts;
using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Gigtime.Server.Tests;

public sealed class PersistenceTests
{
	[Fact]
	public async Task Every_inline_projection_rebuilds_to_the_stored_document_and_events_keep_correlation()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();

		var started = (await app.PostAsync<TaskDto>("/v1/tasks/start",
			new StartTaskRequest("example", "project", Rate: "20"),
			correlationId: "start-correlation")).Data!;

		app.Clock.Now = app.Clock.Now.AddSeconds(45);
		await app.PostAsync<TaskDto>("/v1/tasks/stop", correlationId: "stop-correlation");
		await app.PostAsync<TaskDto>($"/v1/tasks/{started.Id}/submit", correlationId: "submit-correlation");
		await app.PostAsync<TaskDto>($"/v1/tasks/{started.Id}/unsubmit", correlationId: "unsubmit-correlation");

		await app.PostAsync<TaskDto>($"/v1/tasks/{started.Id}/amend",
			new AmendTaskInput(Rate: "21"),
			correlationId: "rate-correlation");

		await app.PostAsync<TaskDto>($"/v1/tasks/{started.Id}/submit", correlationId: "resubmit-correlation");
		var factory = app.Host.Services.GetRequiredService<IDocumentSessionFactory>();
		await using var documents = factory.QuerySession();
		var catalogue = new CatalogueView();
		var tracker = new OpenTask();
		var task = new TaskView();

		foreach (var e in await documents.Events.FetchStreamAsync(CatalogueView.StreamId, token: TestContext.Current.CancellationToken))
		{
			e.CorrelationId.ShouldNotBeNullOrEmpty();
			catalogue.Evolve(e.Data);
		}

		foreach (var e in await documents.Events.FetchStreamAsync(OpenTask.StreamId, token: TestContext.Current.CancellationToken))
		{
			e.CorrelationId.ShouldNotBeNullOrEmpty();
			tracker.Evolve(e.Data);
		}

		var events = await documents.Events.FetchStreamAsync(started.Id, token: TestContext.Current.CancellationToken);

		events
			.Select(e => e.CorrelationId)
			.ShouldBe([
				"start-correlation", "start-correlation", "stop-correlation", "submit-correlation",
				"unsubmit-correlation", "rate-correlation", "resubmit-correlation",
			]);

		foreach (var e in events) task.Evolve(e.Data);
		await using var session = factory.LightweightSession();

		JsonSerializer
			.Serialize(await session.Events.AggregateStreamAsync<CatalogueView>(CatalogueView.StreamId,
				token: TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(catalogue));

		JsonSerializer
			.Serialize(await session.Events.AggregateStreamAsync<OpenTask>(OpenTask.StreamId,
				token: TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(tracker));

		JsonSerializer
			.Serialize(await session.Events.AggregateStreamAsync<TaskView>(started.Id,
				token: TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(task));

		JsonSerializer
			.Serialize(await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(catalogue));

		JsonSerializer
			.Serialize(await documents.LoadAsync<OpenTask>(OpenTask.StreamId, TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(tracker));

		JsonSerializer
			.Serialize(await documents.LoadAsync<TaskView>(started.Id, TestContext.Current.CancellationToken))
			.ShouldBe(JsonSerializer.Serialize(task));
	}

	[Fact]
	public async Task Concurrent_starts_create_exactly_one_task()
	{
		await using var app = await ServiceHost.StartAsync();
		await app.CreateProjectAsync();

		var host = app.Host;

		var responses = await Task.WhenAll(Enumerable
			.Range(0, 2)
			.Select(_ => host.Scenario(s =>
			{
				s.Post.Json(new StartTaskRequest("example", "project")).ToUrl("/v1/tasks/start");
				s.IgnoreStatusCode();
			})));

		var envelopes = await Task.WhenAll(responses.Select(r => r.ReadAsJsonAsync<Envelope<TaskDto>>()));
		envelopes.Count(e => e.Ok).ShouldBe(1);
		// #35: scheduling may let the loser read the committed tracker instead of racing its append.
		envelopes.Single(e => !e.Ok).Error!.Code.ShouldBeOneOf("conflict", "invalid_transition");
		(await app.GetAsync<TasksDto>("/v1/tasks")).Data!.Tasks.ShouldHaveSingleItem();
	}
}
