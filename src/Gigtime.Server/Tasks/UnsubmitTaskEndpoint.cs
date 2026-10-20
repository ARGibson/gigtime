using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class UnsubmitTaskEndpoint
{
	public static Task<LoadedCorrection> LoadAsync(string id,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		return CorrectionWrites.LoadAsync(id,
			"unsubmit",
			events,
			context,
			(_, _, _) => new([new UnsubmitTask(now)]),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCorrection loaded) => loaded.Refusal;

	[WolverinePost("/v1/tasks/{id}/unsubmit")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents) Handle(string id, [NotBody] LoadedCorrection loaded, DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded.Loaded);

		return (Envelopes.Success("unsubmit", TaskResponses.From(task, loaded.Loaded.Catalogue, now)),
			Storage.AppendEvents(task.Id, loaded.Loaded.TaskVersion, TaskWrites.TaskEvents(loaded.Loaded)),
			TaskWrites.TrackerEvents(loaded.Loaded));
	}
}
