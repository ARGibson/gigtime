using Gigtime.Contracts.Reporting;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;

namespace Gigtime.Server.Reporting;

public static class ReportFilters
{
	public static EnvelopeResult? Validate(string command,
		CatalogueView catalogue,
		bool week,
		bool month,
		string? range,
		string? client,
		string? project)
	{
		if ((week ? 1 : 0) + (month ? 1 : 0) + (range is null ? 0 : 1) > 1 || !ReportDates.TryParse(range, out _, out _))
			return new(command, new("validation", "Choose week, month, or an ordered inclusive date range yyyy-MM-dd..yyyy-MM-dd."));

		var foundClient = client is null ? null : catalogue.FindClient(client);

		if (client is not null && foundClient is null)
			return new(command, new("not_found", "The client does not exist."));

		if (project is not null && !catalogue.Projects.Values.Any(p => p.Key == project && (client is null || p.ClientId == foundClient!.Id)))
			return new(command, new("not_found", "The project does not exist."));

		return null;
	}

	public static ReportQuery Query(DateTimeOffset now, CatalogueView catalogue, bool month, string? range, string? client)
	{
		if (!ReportDates.TryParse(range, out var from, out var until)) throw new InvalidOperationException("Report range was not validated.");

		return new(now,
			range is not null ? PeriodKind.Range : month ? PeriodKind.Month : PeriodKind.Week,
			from,
			until,
			client is null ? null : catalogue.FindClient(client)!.Id);
	}

	public static IEnumerable<TaskView> Tasks(LoadedReport loaded, string? project)
	{
		return loaded.Tasks.Where(t => project is null || loaded.Catalogue.Projects[t.ProjectId].Key == project);
	}
}
