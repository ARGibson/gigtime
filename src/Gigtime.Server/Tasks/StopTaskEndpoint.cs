using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class StopTaskEndpoint
{
	public static Task<LoadedTaskCommand> LoadAsync(
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		return TaskWrites.LoadAsync(events, context, (_, _) => new StopTask(now), false, cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskCommand loaded) => EnvelopeResult.From(loaded.Decision, loaded.Tracker.TaskId);

	[WolverinePost("/v1/tasks/stop")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents) Handle([NotBody] LoadedTaskCommand loaded, DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded);

		return (Envelopes.Success("stop", TaskResponses.From(task, loaded.Catalogue, now)),
			Storage.AppendEvents(task.Id, loaded.TaskVersion, TaskWrites.TaskEvents(loaded)), TaskWrites.TrackerEvents(loaded));
	}
}
