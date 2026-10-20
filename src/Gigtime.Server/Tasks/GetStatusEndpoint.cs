using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using Gigtime.Platform;
using JasperFx.Events.Documents;

namespace Gigtime.Server.Tasks;

public static class GetStatusEndpoint
{
	public static async Task<LoadedStatus> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		var catalogue = (await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var tracker = await documents.LoadAsync<OpenTask>(OpenTask.StreamId, cancellationToken);
		var tasks = new List<TaskView>();

		foreach (var id in tracker?.TaskIds ?? [])
		{
			if (await documents.LoadAsync<TaskView>(id, cancellationToken) is { } task) tasks.Add(task);
		}

		return new(catalogue, tasks);
	}

	[WolverineGet("/v1/status")]
	public static Envelope<StatusDto> Handle([NotBody] LoadedStatus loaded, DateTimeOffset now, IPlatformPaths paths)
	{
		var running = loaded.Tasks.FirstOrDefault(t => t.State == TaskState.Running);

		return Envelopes.Success("status",
			new StatusDto(
				running is null ? null : TaskResponses.From(running, loaded.Catalogue, now),
				TaskTargets.Paused(loaded.Tasks).Select(t => TaskResponses.From(t, loaded.Catalogue, now)).ToArray(),
				paths.Instance.Name));
	}
}
