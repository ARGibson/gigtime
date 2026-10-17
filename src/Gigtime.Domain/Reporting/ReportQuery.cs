namespace Gigtime.Domain.Reporting;

public sealed record ReportQuery(
	DateTimeOffset At,
	PeriodKind Kind = PeriodKind.Week,
	DateOnly? From = null,
	DateOnly? UntilExclusive = null,
	Guid? ClientId = null,
	Guid? ProjectId = null);
