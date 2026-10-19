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
		var taskId = tracker.TaskId ?? "";

		if (start)
		{
			for (var attempt = 0; attempt < 1024; attempt++)
			{
				taskId = TaskIds.Create(_ => false);

				if (await events.FetchStreamStateAsync(taskId, cancellationToken) is null) break;

				if (attempt == 1023) throw new InvalidOperationException("Unable to allocate a task id.");
			}
		}

		var taskStream = taskId.Length == 0 ? null : await events.FetchForWriting<TaskView>(taskId, cancellationToken);
		var task = taskStream?.Aggregate ?? new TaskView();
		var intent = command(taskId, catalogue);
		context.Items["command"] = intent.Command;
		var decision = TaskDecider.Decide(task, tracker, catalogue, intent);

		return new(catalogue,
			task,
			tracker,
			taskStream?.StartingVersion ?? 0,
			trackerStream.StartingVersion ?? 0,
			intent,
			decision,
			context.TraceIdentifier);
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
		};

		foreach (var e in loaded.Decision.Events.Where(e => e is not OpenTaskSet and not OpenTaskCleared)) task.Evolve(e);

		return task;
	}

	public static object[] TaskEvents(LoadedTaskCommand loaded)
	{
		return CorrelatedEvents.Wrap(loaded.Decision.Events.Where(e => e is not OpenTaskSet and not OpenTaskCleared), loaded.CorrelationId);
	}

	public static AppendEvents TrackerEvents(LoadedTaskCommand loaded)
	{
		var events = loaded.Decision.Events.Where(e => e is OpenTaskSet or OpenTaskCleared).ToArray();
		// #2: pause and resume also advance the tracker version, serializing every lifecycle command.
		if (events.Length == 0) events = [new OpenTaskSet(loaded.Task.Id)];

		return Storage.AppendEvents(OpenTask.StreamId, loaded.TrackerVersion, CorrelatedEvents.Wrap(events, loaded.CorrelationId));
	}
}
