namespace Gigtime.Contracts.Tasks;

public sealed record StartTaskRequest(string Client, string Project, string? Ref = null);

public sealed record RecordTaskRequest(string Client, string Project, DateTimeOffset Start, DateTimeOffset End, string? Ref = null);

public sealed record AmendTaskRequest(
	int? Interval = null,
	DateTimeOffset? Start = null,
	DateTimeOffset? End = null,
	string? Client = null,
	string? Project = null,
	string? Ref = null,
	bool ClearRef = false);

public sealed record VoidTaskRequest(string? Reason = null);
