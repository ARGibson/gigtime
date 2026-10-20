using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class PauseTaskEndpoint
{
	public static Task<LoadedTaskCommand> LoadAsync(
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		return TaskWrites.LoadAsync(events, context, (_, _) => new PauseTask(now), false, cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskCommand loaded) => EnvelopeResult.From(loaded.Decision, loaded.Task.Id);

	[WolverinePost("/v1/tasks/pause")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents?, AppendEvents) Handle([NotBody] LoadedTaskCommand loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded);

		return (TaskWrites.Response(loaded, now),
			Storage.AppendEvents(task.Id, loaded.TaskVersion, TaskWrites.TaskEvents(loaded)), TaskWrites.OtherEvents(loaded),
			TaskWrites.TrackerEvents(loaded));
	}
}
