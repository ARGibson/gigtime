using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Gigtime.Domain.Tests.Catalogue;
using Shouldly;

namespace Gigtime.Domain.Tests.Tasks;

internal sealed class TaskScenario
{
	public static readonly DateTimeOffset
		At = DateTimeOffset.Parse("2026-03-02T09:00:00Z", System.Globalization.CultureInfo.InvariantCulture);

	public CatalogueScenario Catalogue { get; } = new();
	public TaskView Task { get; } = new();
	public OpenTask Tracker { get; } = new();
	public List<object> TaskEvents { get; } = [];
	public List<object> TrackerEvents { get; } = [];

	public TaskScenario()
	{
		Catalogue.Send(new SetDefaults("UTC"));
		Catalogue.Send(new AddClient(Catalogue.ClientId, "acme", "Acme", "USD"));
		Catalogue.Send(new AddProject(Catalogue.ProjectId, Catalogue.ClientId, "site-audit", Rate: 120m));
	}

	public Decision Decide(TaskCommand command) => TaskDecider.Decide(Task, Tracker, Catalogue.State, command);

	public void Send(TaskCommand command)
	{
		var d = Decide(command);

		d.Error.ShouldBeNull();

		foreach (var e in d.Events)
		{
			if (e is OpenTaskSet or OpenTaskCleared)
			{
				Tracker.Evolve(e);
				TrackerEvents.Add(e);
			}
			else
			{
				Task.Evolve(e);
				TaskEvents.Add(e);
			}
		}
	}

	public void Start() => Send(new StartTask("7f3a9c", Catalogue.ProjectId, "opaque-ref", At));

	public void Reach(TaskState state)
	{
		if (state == TaskState.Missing) return;

		Start();

		if (state == TaskState.Running) return;

		if (state == TaskState.Paused)
		{
			Send(new PauseTask(At.AddSeconds(20)));

			return;
		}

		Send(new StopTask(At.AddSeconds(30)));
		if (state == TaskState.Voided) Send(new VoidTask());
	}
}
