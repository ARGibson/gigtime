using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class AddIntervalEndpoint
{
	public static Task<LoadedCorrection> LoadAsync(string id,
		AddIntervalInput request,
		IEventStoreOperations events,
		[NotBody] HttpContext context,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		if (request is null) throw new BadHttpRequestException("A JSON object is required.");

		return CorrectionWrites.LoadAsync(id,
			"add",
			events,
			context,
			(_, catalogue, task) => Interval(request, catalogue, task, now),
			cancellationToken);
	}

	public static EnvelopeResult? Validate([NotBody] LoadedCorrection loaded) => loaded.Refusal;

	[WolverinePost("/v1/tasks/{id}/add")]
	public static (Envelope<TaskDto>, AppendEvents, AppendEvents) Handle(string id,
		AddIntervalInput request,
		[NotBody] LoadedCorrection loaded,
		DateTimeOffset now)
	{
		return (TaskWrites.Response(loaded.Loaded, now),
			Storage.AppendEvents(loaded.Loaded.Task.Id, loaded.Loaded.TaskVersion, TaskWrites.TaskEvents(loaded.Loaded)),
			TaskWrites.TrackerEvents(loaded.Loaded));
	}

	private static CorrectionIntent Interval(AddIntervalInput request, CatalogueView catalogue, TaskView task, DateTimeOffset now)
	{
		if (task.State is TaskState.Missing or TaskState.Submitted or TaskState.Voided)
			return new([new AddInterval(now, now)]);

		if (request.Ref is not null || request.Rate is not null)
			return new([], new("validation", "Adding an interval takes no ref or rate."));

		var zone = catalogue.Resolve(task.ProjectId).Timezone;

		if (!InputTime.TryResolve(request.Start, zone, now, out var start) || !InputTime.TryResolve(request.End, zone, now, out var end))
			return new([], new("validation", "Invalid or ambiguous time; supply an ISO offset."));

		return new([new AddInterval(start, end)]);
	}
}
