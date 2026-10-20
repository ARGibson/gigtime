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
		return ReportFilters.Validate("report", loaded.Catalogue, week, month, range, client, project);
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
		var query = ReportFilters.Query(now, loaded.Catalogue, month, range, client);
		var tasks = ReportFilters.Tasks(loaded, project);
		var result = SettlementMath.Calculate(tasks, loaded.Catalogue, query, now);

		return Envelopes.Success("report", ReportResponses.From(result, loaded.Catalogue));
	}
}
