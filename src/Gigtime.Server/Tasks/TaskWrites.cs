using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public static class TaskWrites
{
	public static async Task<LoadedTaskCommand> LoadAsync(
		IEventStoreOperations events,
		HttpContext context,
		Func<string, CatalogueView, TaskCommand> command,
		bool start,
		CancellationToken cancellationToken)
	{
		var trackerStream = await events.FetchForWriting<OpenTask>(OpenTask.StreamId, cancellationToken);
		var tracker = trackerStream.Aggregate ?? new OpenTask();
		var catalogue = (await events.FetchLatest<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;
		var open = new List<TaskView>();
		var versions = new Dictionary<string, long>(StringComparer.Ordinal);

		foreach (var id in tracker.TaskIds)
		{
			var stream = await events.FetchForWriting<TaskView>(id, cancellationToken);
			if (stream.Aggregate is { } task) open.Add(task);
			versions[id] = stream.StartingVersion ?? 0;
		}

		var taskId = context.Request.Query["task"].FirstOrDefault();
		Rejection? choiceError = null;

		if (start)
		{
			for (var attempt = 0; ; attempt++)
			{
				taskId = TaskIds.Create(_ => false);

				if (await events.FetchStreamStateAsync(taskId, cancellationToken) is null) break;

				if (attempt == 1023) throw new InvalidOperationException("Unable to allocate a task id.");
			}
		}
		else if (taskId is null)
		{
			var choice = TaskTargets.Choose(command("", catalogue).Command, tracker, open);
			taskId = choice.TaskId;
			choiceError = choice.Error;
		}

		var taskStream = string.IsNullOrEmpty(taskId) ? null : await events.FetchForWriting<TaskView>(taskId, cancellationToken);
		var selected = taskStream?.Aggregate ?? new TaskView();
		var intent = command(taskId ?? "", catalogue);
		context.Items["command"] = intent.Command;

		var decision = choiceError is null
			? TaskDecider.Decide(selected, tracker, catalogue, intent, open)
			: new Decision([], choiceError);

		return new(catalogue,
			selected,
			tracker,
			taskStream?.StartingVersion ?? 0,
			trackerStream.StartingVersion ?? 0,
			intent,
			decision,
			context.TraceIdentifier,
			open,
			versions);
	}

	public static TaskView After(LoadedTaskCommand loaded)
	{
		var old = loaded.Task;

		var task = new TaskView
		{
			Id = old.Id,
			ProjectId = old.ProjectId,
			Ref = old.Ref,
			State = old.State,
			Timeline = [.. old.Timeline],
			Rate = old.Rate,
			Submission = old.Submission,
			StartedAt = old.StartedAt,
			StoppedAt = old.StoppedAt,
			PausedAt = old.PausedAt,
		};

		foreach (var e in loaded.Decision.Events.Where(e => !IsTracker(e) && e is not TaskEventFor)) task.Evolve(e);

		return task;
	}

	public static object[] TaskEvents(LoadedTaskCommand loaded) =>
		CorrelatedEvents.Wrap(loaded.Decision.Events.Where(e => !IsTracker(e) && e is not TaskEventFor), loaded.CorrelationId);

	public static AppendEvents? OtherEvents(LoadedTaskCommand loaded)
	{
		var changes = loaded.Decision.Events.OfType<TaskEventFor>().ToArray();

		if (changes.Length == 0) return null;

		var id = changes[0].TaskId;

		if (changes.Any(e => e.TaskId != id)) throw new InvalidOperationException("A switch can change only one other task.");

		return Storage.AppendEvents(id, loaded.Versions![id], CorrelatedEvents.Wrap(changes.Select(e => e.Event), loaded.CorrelationId));
	}

	public static AppendEvents TrackerEvents(LoadedTaskCommand loaded)
	{
		var changes = loaded.Decision.Events.Where(IsTracker).ToArray();
		if (changes.Length == 0) changes = [new TrackerAdvanced()];

		return Storage.AppendEvents(OpenTask.StreamId, loaded.TrackerVersion, CorrelatedEvents.Wrap(changes, loaded.CorrelationId));
	}

	public static Envelope<TaskDto> Response(LoadedTaskCommand loaded, DateTimeOffset now) =>
		Envelopes.Success(loaded.Command.Command,
			TaskResponses.From(After(loaded), loaded.Catalogue, now),
			loaded.Decision.Warnings?.Select(w => new WarningDto(w.Code, w.Message)).ToArray());

	private static bool IsTracker(object value) => value is TaskOpened or TaskClosed or TrackerAdvanced or OpenTaskSet or OpenTaskCleared;
}
