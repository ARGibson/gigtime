using Gigtime.Contracts.Reporting;
using Gigtime.Domain.Reporting;
using Gigtime.Domain.Tasks;
using JasperFx.Events.Documents;
using Microsoft.AspNetCore.Mvc;

namespace Gigtime.Server.Reporting;

public static class GetReportEndpoint
{
	public static async Task<LoadedReport> LoadAsync(IDocumentReadOperations documents, CancellationToken cancellationToken)
	{
		return new((await documents.LoadAsync<CatalogueView>(CatalogueView.StreamId, cancellationToken))!,
			await documents.Query<TaskView>().ToListAsync(cancellationToken));
	}

	public static EnvelopeResult? Validate([NotBody] LoadedReport loaded,
		[FromQuery] bool week = false,
		[FromQuery] bool month = false,
		[FromQuery] string? range = null,
		[FromQuery] string? client = null,
		[FromQuery] string? project = null)
	{
		if ((week ? 1 : 0) + (month ? 1 : 0) + (range is null ? 0 : 1) > 1 || !ReportDates.TryParse(range, out _, out _))
			return new("report", new("validation", "Choose week, month, or an ordered inclusive date range yyyy-MM-dd..yyyy-MM-dd."));

		if (client is not null && loaded.Catalogue.FindClient(client) is null)
			return new("report", new("not_found", "The client does not exist."));

		if (project is not null && !loaded.Catalogue.Projects.Values.Any(p => p.Key == project
					&& (client is null || p.ClientId == loaded.Catalogue.FindClient(client)!.Id)))
		{
			return new("report", new("not_found", "The project does not exist."));
		}

		return null;
	}

	[WolverineGet("/v1/report")]
	public static Envelope<ReportDto> Handle([NotBody] LoadedReport loaded,
		DateTimeOffset now,
		[FromQuery] bool week = false,
		[FromQuery] bool month = false,
		[FromQuery] string? range = null,
		[FromQuery] string? client = null,
		[FromQuery] string? project = null)
	{
		if (!ReportDates.TryParse(range, out var from, out var until)) throw new InvalidOperationException("Report range was not validated.");

		var query = new ReportQuery(now,
			range is not null ? PeriodKind.Range : month ? PeriodKind.Month : PeriodKind.Week,
			from,
			until,
			client is null ? null : loaded.Catalogue.FindClient(client)!.Id);

		var tasks = loaded.Tasks.Where(t => project is null || loaded.Catalogue.Projects[t.ProjectId].Key == project);
		var totals = ReportMath.Calculate(tasks, loaded.Catalogue, query);

		return Envelopes.Success("report", ReportResponses.From(totals, loaded.Catalogue));
	}
}
