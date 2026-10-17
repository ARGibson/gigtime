using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public sealed record LoadedTaskList(CatalogueView Catalogue, IReadOnlyList<TaskView> Tasks);
