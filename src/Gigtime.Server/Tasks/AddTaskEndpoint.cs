using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class AddTaskEndpoint
{
	public static Task<LoadedCorrection> LoadAsync(RecordTaskInput request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CorrectionWrites.LoadAsync(null,
			"add",
			events,
			context,
			(id, catalogue, _) => CorrectionWrites.Record(id, catalogue, request, now),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCorrection loaded) => loaded.Refusal;

	[WolverinePost("/v1/tasks/add")]
	public static (Envelope<TaskDto>, StartStream) Handle(RecordTaskInput request, [NotBody] LoadedCorrection loaded, DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded.Loaded);

		return (Envelopes.Success("add", TaskResponses.From(task, loaded.Loaded.Catalogue, now)),
			Storage.StartStream<TaskView>(task.Id, TaskWrites.TaskEvents(loaded.Loaded)));
	}
}
