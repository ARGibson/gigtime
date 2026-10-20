using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public sealed record LoadedTaskCommand(
	CatalogueView Catalogue,
	TaskView Task,
	OpenTask Tracker,
	long TaskVersion,
	long TrackerVersion,
	TaskCommand Command,
	Decision Decision,
	string CorrelationId,
	IReadOnlyList<TaskView>? OpenTasks = null,
	IReadOnlyDictionary<string, long>? Versions = null);
