using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Gigtime.Server.Tasks;

public static class ListTasksEndpoint
{
	public static async Task<LoadedTaskList> LoadAsync(
		IDocumentReadOperations documents,
		CancellationToken cancellationToken,
		[FromQuery] bool all = false,
		[FromQuery] string? client = null,
		[FromQuery] string? project = null)
	{
		var catalogue = (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var clientId = client is null ? null : catalogue.FindClient(client)?.Id;

		var ids = catalogue
			.Projects.Values
			.Where(p => (client is null || p.ClientId == clientId) && (project is null || p.Key == project))
			.Select(p => p.Id)
			.ToHashSet();

		var tasks = await documents.Query<TaskView>().ToListAsync(cancellationToken);

		return new(catalogue, tasks.Where(t => ids.Contains(t.ProjectId) && (all || t.State != TaskState.Voided)).ToArray());
	}

	public static EnvelopeResult? Validate(
		[NotBody] LoadedTaskList loaded,
		[FromQuery] int? last = null,
		[FromQuery] string? client = null,
		[FromQuery] string? project = null)
	{
		if (last is <= 0) return new("log", new("validation", "last must be a positive integer."));

		var clientId = client is null ? null : loaded.Catalogue.FindClient(client)?.Id;

		if (client is not null && clientId is null) return new("log", new("not_found", "The client does not exist."));

		if (project is not null && !loaded.Catalogue.Projects.Values.Any(p => p.Key == project && (client is null || p.ClientId == clientId)))
			return new("log", new("not_found", "The project does not exist."));

		return null;
	}

	[WolverineGet("/v1/tasks")]
	public static Envelope<TasksDto> Handle(
		[NotBody] LoadedTaskList loaded,
		DateTimeOffset now,
		[FromQuery] int? last = null,
		[FromQuery] bool all = false,
		[FromQuery] string? client = null,
		[FromQuery] string? project = null)
	{
		var tasks = loaded
			.Tasks.OrderByDescending(t => t.StartedAt)
			.ThenByDescending(t => t.Id)
			.Take(last ?? 10)
			.Select(t => TaskResponses.From(t, loaded.Catalogue, now))
			.ToArray();

		return Envelopes.Success("log", new TasksDto(tasks));
	}
}
