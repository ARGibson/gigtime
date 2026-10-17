using Gigtime.Contracts.Catalogue;

namespace Gigtime.Contracts.Reporting;

public sealed record EarningsDto(string Currency, string Unrounded, string Rounded);

public sealed record ReportTotalDto(
	EntityDto Client,
	EntityDto Project,
	string Currency,
	long RawSeconds,
	long RoundedSeconds,
	string Display,
	EarningsDto? EstimatedEarnings);

public sealed record ClientReportTotalDto(
	EntityDto Client,
	string Currency,
	long RawSeconds,
	long RoundedSeconds,
	string Display,
	EarningsDto? EstimatedEarnings);

public sealed record ReportDto(IReadOnlyList<ReportTotalDto> Totals, IReadOnlyList<ClientReportTotalDto> Clients);
