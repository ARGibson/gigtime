namespace Gigtime.Domain.Tasks;

public abstract record TaskCommand(string Command);

public sealed record StartTask(string TaskId, Guid ProjectId, string? Ref, DateTimeOffset At, decimal? Rate = null, bool Switch = false)
	: TaskCommand("start");

public sealed record PauseTask(DateTimeOffset At) : TaskCommand("pause");

public sealed record ResumeTask(DateTimeOffset At, bool Switch = false) : TaskCommand("resume");

public sealed record StopTask(DateTimeOffset At, bool Submit = false) : TaskCommand("stop");

public sealed record RecordTask(string TaskId, Guid ProjectId, string? Ref, DateTimeOffset Start, DateTimeOffset End, decimal? Rate = null)
	: TaskCommand("add");

public sealed record AmendInterval(int Interval, DateTimeOffset? Start = null, DateTimeOffset? End = null) : TaskCommand("amend");

public sealed record ReassignTask(Guid ProjectId, Change<decimal?> Rate = default) : TaskCommand("amend");

public sealed record ChangeTaskRef(string? Ref) : TaskCommand("amend");

public sealed record VoidTask(string? Reason = null) : TaskCommand("void");

public sealed record SubmitTask(DateTimeOffset At) : TaskCommand("submit");

public sealed record UnsubmitTask(DateTimeOffset At) : TaskCommand("unsubmit");

public sealed record SetTaskRate(decimal? Rate) : TaskCommand("amend");

public sealed record AddInterval(DateTimeOffset Start, DateTimeOffset End) : TaskCommand("add");
