using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class ResumeTaskEndpoint
{
	public static Task<LoadedTaskCommand> LoadAsync(
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		return TaskWrites.LoadAsync(events,
			context,
			(_, _) => new ResumeTask(now, bool.TryParse(context.Request.Query["switch"], out var switchTask) && switchTask),
			false,
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedTaskCommand loaded, [NotBody] HttpContext context)
	{
		if (context.Request.Query.TryGetValue("switch", out var value) && !bool.TryParse(value, out _))
			return new("resume", new("validation", "switch must be true or false."));

		return EnvelopeResult.From(loaded.Decision, loaded.Task.Id);
	}

	[WolverinePost("/v1/tasks/resume")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents?, AppendEvents) Handle([NotBody] LoadedTaskCommand loaded,
		DateTimeOffset now)
	{
		var task = TaskWrites.After(loaded);

		return (TaskWrites.Response(loaded, now),
			Storage.AppendEvents(task.Id, loaded.TaskVersion, TaskWrites.TaskEvents(loaded)), TaskWrites.OtherEvents(loaded),
			TaskWrites.TrackerEvents(loaded));
	}
}
