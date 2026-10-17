using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Reporting;

public sealed record LoadedReport(CatalogueView Catalogue, IReadOnlyList<TaskView> Tasks);
