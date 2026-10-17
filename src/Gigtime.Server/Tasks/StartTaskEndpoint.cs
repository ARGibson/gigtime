using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class StartTaskEndpoint
{
	public static Task<LoadedTaskCommand> LoadAsync(
		StartTaskRequest request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return TaskWrites.LoadAsync(events,
			context,
			(id, catalogue) => new StartTask(id,
				catalogue.FindProject(catalogue.FindClient(request.Client)?.Id ?? Guid.Empty, request.Project)?.Id ?? Guid.Empty,
				request.Ref,
				now),
			true,
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskCommand loaded) => EnvelopeResult.From(loaded.Decision, loaded.Tracker.TaskId);

	[WolverinePost("/v1/tasks/start")]
	public static (Envelope<TaskDto>, StartStream, AppendEvents) Handle(
		StartTaskRequest request,
		[NotBody] LoadedTaskCommand loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded);

		return (Envelopes.Success("start", TaskResponses.From(task, loaded.Catalogue, now)),
			Storage.StartStream<TaskView>(task.Id, TaskWrites.TaskEvents(loaded)), TaskWrites.TrackerEvents(loaded));
	}
}
