using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Tasks;

public static class GetTaskEndpoint
{
	public static async Task<LoadedTaskRead> LoadAsync(string id, IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		var catalogue = (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var task = await documents.LoadAsync<TaskView>(id, cancellationToken);

		return new(catalogue, task);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskRead loaded)
	{
		return loaded.Task is null ? new("show", new("not_found", "The task does not exist.")) : null;
	}

	[WolverineGet("/v1/tasks/{id}")]
	public static Envelope<TaskDto> Handle(string id, [NotBody] LoadedTaskRead loaded, DateTimeOffset now)
	{
		return Envelopes.Success("show", TaskResponses.From(loaded.Task!, loaded.Catalogue, now));
	}
}
