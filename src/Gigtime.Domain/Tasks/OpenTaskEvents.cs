namespace Gigtime.Domain.Tasks;

public sealed record OpenTaskSet(string TaskId);

public sealed record OpenTaskCleared;

public sealed record TaskOpened(string TaskId);

public sealed record TaskClosed(string TaskId);

public sealed record TrackerAdvanced;
