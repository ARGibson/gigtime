using System.Globalization;
using Gigtime.Domain.Catalogue;
using Gigtime.Domain.Tasks;
using Gigtime.Domain.Tests.Catalogue;
using Shouldly;

namespace Gigtime.Domain.Tests.Reporting;

internal sealed class ReportScenario
{
	private readonly Dictionary<string, OpenTask> trackers = [];
	internal CatalogueScenario Catalogue { get; } = new();
	internal List<TaskView> Tasks { get; } = [];
	internal static DateTimeOffset Now => At("2026-03-05T12:00:00Z");

	internal ReportScenario(string timezone = "UTC")
	{
		Catalogue.Send(new SetDefaults("UTC"));
		Catalogue.Send(new AddClient(Catalogue.ClientId, "acme", "Acme", "USD", Timezone: timezone));
		Catalogue.Send(new AddProject(Catalogue.ProjectId, Catalogue.ClientId, "work", Rate: 120m));
	}

	internal TaskView Start(string id, string start, Guid? project = null, decimal? rate = null)
	{
		var task = new TaskView();
		trackers.Add(id, new());
		Tasks.Add(task);
		Apply(task, trackers[id], new StartTask(id, project ?? Catalogue.ProjectId, "ref-" + id, At(start), rate));

		return task;
	}

	internal TaskView Record(string id, string start, string end, Guid? project = null, decimal? rate = null)
	{
		var task = new TaskView();
		trackers.Add(id, new());
		Tasks.Add(task);
		Apply(task, trackers[id], new RecordTask(id, project ?? Catalogue.ProjectId, "ref-" + id, At(start), At(end), rate));

		return task;
	}

	internal void Send(TaskView task, TaskCommand command) => Apply(task, trackers[task.Id], command);

	internal static DateTimeOffset At(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture);

	private void Apply(TaskView task, OpenTask tracker, TaskCommand command)
	{
		var result = TaskDecider.Decide(task, tracker, Catalogue.State, command);
		result.Error.ShouldBeNull();

		foreach (var e in result.Events)
		{
			if (e is OpenTaskSet or OpenTaskCleared) tracker.Evolve(e);
			else task.Evolve(e);
		}
	}
}
