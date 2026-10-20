using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using Gigtime.Server.Catalogue;

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
				now,
				CatalogueWrites.Rate(request.Rate),
				request.Switch,
				request.SwitchFrom),
			true,
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskCommand loaded) => EnvelopeResult.From(loaded.Decision, loaded.Task.Id);

	[WolverinePost("/v1/tasks/start")]
	public static (Envelope<TaskDto>, StartStream, AppendEvents?, AppendEvents) Handle(
		StartTaskRequest request,
		[NotBody] LoadedTaskCommand loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded);

		return (TaskWrites.Response(loaded, now),
			Storage.StartStream<TaskView>(task.Id, TaskWrites.TaskEvents(loaded)), TaskWrites.OtherEvents(loaded),
			TaskWrites.TrackerEvents(loaded));
	}
}
