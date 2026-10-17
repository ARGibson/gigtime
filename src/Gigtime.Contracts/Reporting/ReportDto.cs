using Gigtime.Contracts.Catalogue;

namespace Gigtime.Contracts.Reporting;

public sealed record ReportTotalDto(EntityDto Client, EntityDto Project, string Currency,
	long RawSeconds, long RoundedSeconds, string Display, MoneyDto? EstimatedEarnings);
public sealed record ReportDto(IReadOnlyList<ReportTotalDto> Totals);
