using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using Gigtime.Platform;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Tasks;

public static class GetStatusEndpoint
{
	public static async Task<LoadedTaskRead> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		var catalogue = (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var tracker = await documents.LoadAsync<OpenTask>(OpenTask.StreamId, cancellationToken);
		var task = tracker?.TaskId is { } id ? await documents.LoadAsync<TaskView>(id, cancellationToken) : null;

		return new(catalogue, task);
	}

	[WolverineGet("/v1/status")]
	public static Envelope<StatusDto> Handle([NotBody] LoadedTaskRead loaded, DateTimeOffset now, IPlatformPaths paths)
	{
		return Envelopes.Success("status",
			new StatusDto(loaded.Task is { } task ? TaskResponses.From(task, loaded.Catalogue, now) : null, paths.Instance.Name));
	}
}
