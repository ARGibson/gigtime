namespace Gigtime.Domain.Tasks;

/// <summary>The tracker stream is appended atomically with lifecycle events by the host (#2).</summary>
public sealed class OpenTask
{
	public const string StreamId = "tracker";
	public string Id { get; init; } = StreamId;
	public string? TaskId { get; set; }
	public void Apply(OpenTaskSet e) => TaskId = e.TaskId;
	public void Apply(OpenTaskCleared _) => TaskId = null;

	public void Evolve(object e)
	{
		switch (e)
		{
			case OpenTaskSet x: Apply(x); break;
			case OpenTaskCleared x: Apply(x); break;
			default: throw new ArgumentException("Not a tracker event.", nameof(e));
		}
	}
}
