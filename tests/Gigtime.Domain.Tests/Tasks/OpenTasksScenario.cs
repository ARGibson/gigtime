using Gigtime.Domain.Tasks;
using Shouldly;

namespace Gigtime.Domain.Tests.Tasks;

internal sealed class OpenTasksScenario
{
	internal TaskScenario Seed { get; } = new();
	internal OpenTask Tracker { get; } = new();
	internal Dictionary<string, TaskView> Tasks { get; } = [];
	internal Dictionary<string, List<object>> TaskEvents { get; } = [];
	internal List<object> TrackerEvents { get; } = [];
	internal IReadOnlyList<TaskView> OpenViews => Tracker.TaskIds.Select(id => Tasks[id]).ToArray();

	internal Decision Decide(string id, TaskCommand command) => TaskDecider.Decide(
		Tasks.GetValueOrDefault(id) ?? new(),
		Tracker,
		Seed.Catalogue.State,
		command,
		OpenViews);

	internal Decision Send(string id, TaskCommand command)
	{
		var result = Decide(id, command);
		result.Error.ShouldBeNull();
		if (!Tasks.ContainsKey(id)) Tasks[id] = new();

		foreach (var e in result.Events)
		{
			if (e is TaskOpened or TaskClosed or TrackerAdvanced)
			{
				Tracker.Evolve(e);
				TrackerEvents.Add(e);
			}
			else if (e is TaskEventFor other)
			{
				Apply(other.TaskId, other.Event);
			}
			else
			{
				Apply(id, e);
			}
		}

		return result;
	}

	internal Decision Start(string id, DateTimeOffset at, bool switching = false, string? reference = null, Guid? project = null) =>
		Send(id, new StartTask(id, project ?? Seed.Catalogue.ProjectId, reference, at, Switch: switching));

	private void Apply(string id, object e)
	{
		Tasks[id].Evolve(e);
		if (!TaskEvents.TryGetValue(id, out var events)) TaskEvents[id] = events = [];
		events.Add(e);
	}
}
