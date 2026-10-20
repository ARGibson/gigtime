namespace Gigtime.Contracts.Tasks;

public sealed record StartTaskRequest(
	string Client,
	string Project,
	string? Ref = null,
	string? Rate = null,
	bool Switch = false,
	string? SwitchFrom = null);

public sealed record VoidTaskRequest(string? Reason = null);

// #4: local wire times are resolved only after the service loads the client's timezone.
public sealed record RecordTaskInput(string Client, string Project, string Start, string End, string? Ref = null, string? Rate = null);

public sealed record AmendTaskInput(
	int? Interval = null,
	string? Start = null,
	string? End = null,
	string? Client = null,
	string? Project = null,
	string? Ref = null,
	bool ClearRef = false,
	string? Rate = null,
	bool ClearRate = false);

public sealed record AddIntervalInput(string Start, string End, string? Ref = null, string? Rate = null);
