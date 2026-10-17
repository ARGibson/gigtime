namespace Gigtime.Domain.Tasks;

public sealed record TaskStarted(string TaskId, Guid ProjectId, string? Ref, DateTimeOffset At);
public sealed record TaskPaused(DateTimeOffset At);
public sealed record TaskResumed(DateTimeOffset At);
public sealed record TaskStopped(DateTimeOffset At, string Rounding, decimal? Rate, string Currency);
public sealed record TaskRecorded(string TaskId, Guid ProjectId, string? Ref, DateTimeOffset Start, DateTimeOffset End, string Rounding, decimal? Rate, string Currency);
public sealed record IntervalAmended(int Interval, DateTimeOffset? Start = null, DateTimeOffset? End = null);
public sealed record TaskReassigned(Guid ProjectId);
public sealed record TaskRefChanged(string? Ref);
public sealed record TaskVoided(string? Reason = null);
public sealed record OpenTaskSet(string TaskId);
public sealed record OpenTaskCleared;
