using System.Text.Json.Serialization;

namespace Gigtime.Domain.Tasks;

/// <summary>The tracker stream is appended atomically with lifecycle events by the host.</summary>
public sealed class OpenTask
{
	public const string StreamId = "tracker";
	public string Id { get; init; } = StreamId;
	public List<string> TaskIds { get; set; } = [];

	// #36: old inline documents used this property; new documents write only membership.
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string? TaskId
	{
		get => null;
		set => TaskIds = value is null ? [] : [value];
	}

	public void Apply(OpenTaskSet e) => TaskIds = [e.TaskId];
	public void Apply(OpenTaskCleared _) => TaskIds.Clear();

	public void Apply(TaskOpened e)
	{
		if (!TaskIds.Contains(e.TaskId)) TaskIds.Add(e.TaskId);
	}

	public void Apply(TaskClosed e) => TaskIds.Remove(e.TaskId);

	public void Evolve(object e)
	{
		switch (e)
		{
			case OpenTaskSet x: Apply(x); break;
			case OpenTaskCleared x: Apply(x); break;
			case TaskOpened x: Apply(x); break;
			case TaskClosed x: Apply(x); break;
			case TrackerAdvanced: break;
			default: throw new ArgumentException("Not a tracker event.", nameof(e));
		}
	}
}
