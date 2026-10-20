using Gigtime.Contracts.Tasks;

namespace Gigtime.Server.Tasks;

public static class AmendTaskEndpoint
{
	public static Task<LoadedCorrection> LoadAsync(string id,
		AmendTaskInput request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CorrectionWrites.LoadAsync(id,
			"amend",
			events,
			context,
			(_, catalogue, task) => CorrectionWrites.Amend(request, catalogue, task, now),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCorrection loaded) => loaded.Refusal;

	[WolverinePost("/v1/tasks/{id}/amend")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents) Handle(string id,
		AmendTaskInput request,
		[NotBody] LoadedCorrection loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded.Loaded);

		return (Envelopes.Success("amend", TaskResponses.From(task, loaded.Loaded.Catalogue, now)),
			Storage.AppendEvents(task.Id, loaded.Loaded.TaskVersion, TaskWrites.TaskEvents(loaded.Loaded)),
			TaskWrites.TrackerEvents(loaded.Loaded));
	}
}
