using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class VoidTaskEndpoint
{
	public static Task<LoadedCorrection> LoadAsync(string id,
		VoidTaskRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CorrectionWrites.LoadAsync(id,
			"void",
			events,
			context,
			(_, _, _) => new([new VoidTask(request.Reason)]),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCorrection loaded) => loaded.Refusal;

	[WolverinePost("/v1/tasks/{id}/void")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents) Handle(string id,
		VoidTaskRequest request,
		[NotBody] LoadedCorrection loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded.Loaded);

		return (Envelopes.Success("void", TaskResponses.From(task, loaded.Loaded.Catalogue, now)),
			Storage.AppendEvents(task.Id, loaded.Loaded.TaskVersion, TaskWrites.TaskEvents(loaded.Loaded)),
			TaskWrites.TrackerEvents(loaded.Loaded));
	}
}
