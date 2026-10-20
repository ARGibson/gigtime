namespace Gigtime.Domain.Reporting;

public sealed record TimesheetResult(
	IReadOnlyList<TimesheetDay> Days,
	IReadOnlyList<TimesheetProjectTotal> Projects,
	IReadOnlyList<TimesheetClientTotal> Clients);

public sealed record TimesheetDay(DateOnly Date, long RawSeconds, IReadOnlyList<TimesheetTask> Tasks);

public sealed record TimesheetTask(string TaskId, Guid ClientId, Guid ProjectId, string? Ref, long RawSeconds);

public sealed record TimesheetProjectTotal(Guid ClientId, Guid ProjectId, long RawSeconds);

public sealed record TimesheetClientTotal(Guid ClientId, long RawSeconds);
