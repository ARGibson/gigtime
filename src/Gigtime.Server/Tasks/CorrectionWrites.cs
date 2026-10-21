using Gigtime.Contracts.Tasks;
using Gigtime.Domain.Tasks;
using Gigtime.Server.Catalogue;

namespace Gigtime.Server.Tasks;

public static class CorrectionWrites
{
	public static async Task<LoadedCorrection> LoadAsync(string? id,
		string command,
		IEventStoreOperations events,
		HttpContext context,
		Func<string, CatalogueView, TaskView, CorrectionIntent> intent,
		CancellationToken cancellationToken)
	{
		context.Items["command"] = command;
		var trackerStream = await events.FetchForWriting<OpenTask>(OpenTask.StreamId, cancellationToken);
		var tracker = trackerStream.Aggregate ?? new OpenTask();
		var open = new List<TaskView>();

		foreach (var openId in tracker.TaskIds)
		{
			if (await events.FetchLatest<TaskView>(openId, cancellationToken) is { } view) open.Add(view);
		}

		var catalogue = (await events.FetchLatest<CatalogueView>(CatalogueView.StreamId, cancellationToken))!;

		if (id is null)
		{
			for (var attempt = 0; ; attempt++)
			{
				id = TaskIds.Create(_ => false);

				if (await events.FetchStreamStateAsync(id, cancellationToken) is null) break;

				if (attempt == 1023) throw new InvalidOperationException("Unable to allocate a task id.");
			}
		}

		var stream = await events.FetchForWriting<TaskView>(id, cancellationToken);
		var task = stream.Aggregate ?? new TaskView();
		var requested = intent(id, catalogue, task);
		var decision = Decision.Accept();

		var loaded = new LoadedTaskCommand(catalogue,
			task,
			tracker,
			stream.StartingVersion ?? 0,
			trackerStream.StartingVersion ?? 0,
			requested.Commands.Count == 0 ? new ChangeTaskRef(null) : requested.Commands[0],
			decision,
			context.TraceIdentifier,
			open);

		if (requested.Error is { } error) return new(loaded, new(command, error));

		var changes = new List<object>();
		var warnings = new List<DecisionWarning>();

		foreach (var next in requested.Commands)
		{
			decision = TaskDecider.Decide(TaskWrites.After(loaded with
			{
				Decision = Decision.Accept(changes.ToArray())
			}),
				loaded.Tracker,
				catalogue,
				next,
				open);

			if (decision.Error is not null) return new(loaded, EnvelopeResult.From(decision, id));

			changes.AddRange(decision.Events);
			if (decision.Warnings is { } notices) warnings.AddRange(notices);
		}

		return new(loaded with
		{
			Decision = new Decision(changes, Warnings: warnings)
		},
			null);
	}

	public static CorrectionIntent Amend(AmendTaskInput request, CatalogueView catalogue, TaskView task, DateTimeOffset now)
	{
		var commands = new List<TaskCommand>();
		var projectId = task.ProjectId;

		if (task.State is TaskState.Missing or TaskState.Submitted) return new([new ChangeTaskRef(null)]);
		if (request.Rate is not null && request.ClearRate) return Invalid("rate and clear_rate cannot be combined.");

		var rate = request.Rate is not null || request.ClearRate
			? Change.Set(CatalogueWrites.Rate(request.Rate))
			: default(Change<decimal?>);

		if (request.Ref is not null && request.ClearRef) return Invalid("ref and clear_ref cannot be combined.");

		if (request.Client is not null || request.Project is not null)
		{
			if (request.Client is null || request.Project is null) return Invalid("Supply both client and project.");

			var project = catalogue.FindProject(catalogue.FindClient(request.Client)?.Id ?? Guid.Empty, request.Project);

			if (project is null) return new([], new("not_found", "The client or project does not exist."));

			projectId = project.Id;
			commands.Add(new ReassignTask(project.Id, rate));
		}

		else if (rate.IsSpecified)
		{
			commands.Add(new SetTaskRate(rate.Value));
		}

		if (request.Start is not null || request.End is not null)
		{
			if (request.Interval is null) return Invalid("Supply an interval number for time edits.");

			var zone = catalogue.Resolve(projectId).Timezone;
			DateTimeOffset? start = null;
			DateTimeOffset? end = null;

			if (request.Start is { } from)
			{
				if (!InputTime.TryResolve(from, zone, now, out var instant)) return Invalid("Invalid or ambiguous start; supply an ISO offset.");

				start = instant;
			}

			if (request.End is { } to)
			{
				if (!InputTime.TryResolve(to, zone, now, out var instant)) return Invalid("Invalid or ambiguous end; supply an ISO offset.");

				end = instant;
			}

			commands.Add(new AmendInterval(request.Interval.Value, start, end));
		}
		else if (request.Interval is not null)
		{
			return Invalid("Supply start or end with an interval number.");
		}

		if (request.Ref is not null || request.ClearRef) commands.Add(new ChangeTaskRef(request.Ref));

		return commands.Count == 0 ? Invalid("Supply a correction.") : new(commands);
	}

	public static CorrectionIntent Record(string id, CatalogueView catalogue, RecordTaskInput request, DateTimeOffset now)
	{
		var project = catalogue.FindProject(catalogue.FindClient(request.Client)?.Id ?? Guid.Empty, request.Project);

		if (project is null) return new([], new("not_found", "The client or project does not exist."));

		var zone = catalogue.Resolve(project.Id).Timezone;

		if (!InputTime.TryResolve(request.Start, zone, now, out var start) || !InputTime.TryResolve(request.End, zone, now, out var end))
			return Invalid("Invalid or ambiguous time; supply an ISO offset.");

		return new([new RecordTask(id, project.Id, request.Ref, start, end, CatalogueWrites.Rate(request.Rate))]);
	}

	private static CorrectionIntent Invalid(string message) => new([], new("validation", message));
}
