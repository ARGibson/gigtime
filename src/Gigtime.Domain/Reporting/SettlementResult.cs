using Gigtime.Domain.Tasks;

namespace Gigtime.Domain.Reporting;

public sealed record SettlementResult(
	IReadOnlyList<ReportTotal> Totals,
	IReadOnlyList<ClientReportTotal> Clients,
	IReadOnlyList<UnsubmittedTask> Unsubmitted);

public sealed record ReportTotal(Guid ClientId, Guid ProjectId, string Currency, long RawSeconds, long RoundedSeconds, decimal? Earnings);

public sealed record ClientReportTotal(Guid ClientId, string Currency, long RawSeconds, long RoundedSeconds, decimal? Earnings);

public sealed record UnsubmittedTask(
	string TaskId,
	Guid ClientId,
	Guid ProjectId,
	string? Ref,
	TaskState State,
	TaskFigures Figures,
	TaskRate? Rate);
