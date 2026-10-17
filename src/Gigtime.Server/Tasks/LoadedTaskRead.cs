using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Tasks;

public sealed record LoadedTaskRead(CatalogueView Catalogue, TaskView? Task);
