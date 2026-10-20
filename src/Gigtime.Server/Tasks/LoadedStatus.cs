using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public sealed record LoadedStatus(CatalogueView Catalogue, IReadOnlyList<TaskView> Tasks);
