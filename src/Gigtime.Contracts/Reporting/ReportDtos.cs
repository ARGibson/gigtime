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
	EarningsDto? Amount);

public sealed record ClientReportTotalDto(
	EntityDto Client,
	string Currency,
	long RawSeconds,
	long RoundedSeconds,
	string Display,
	EarningsDto? Amount);

public sealed record UnsubmittedTaskDto(
	string TaskId,
	EntityDto Client,
	EntityDto Project,
	string? Ref,
	string State,
	long RawSeconds,
	long RoundedSeconds,
	string Display,
	EarningsDto? Amount);

public sealed record ReportDto(
	IReadOnlyList<ReportTotalDto> Totals,
	IReadOnlyList<ClientReportTotalDto> Clients,
	IReadOnlyList<UnsubmittedTaskDto> Unsubmitted);
