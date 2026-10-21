namespace Gigtime.Domain;

/// <summary>Deciders produce events without mutating the state they were given.</summary>
public sealed record Decision(IReadOnlyList<object> Events, Rejection? Error = null, IReadOnlyList<DecisionWarning>? Warnings = null)
{
	public bool IsSuccess => Error is null;
	public static Decision Accept(params object[] events) => new(events);

	public static Decision Refuse(string code,
		string message,
		string state,
		string command,
		string? hint = null,
		string? taskId = null,
		IReadOnlyList<string>? tasks = null)
	{
		return new([], new(code, message, state, command, hint, taskId, tasks));
	}
}

/// <summary>A refusal carries enough state to explain the next useful command.</summary>
public sealed record Rejection(
	string Code,
	string Message,
	string State,
	string Command,
	string? Hint = null,
	string? TaskId = null,
	IReadOnlyList<string>? Tasks = null);

public sealed record DecisionWarning(string Code, string Message);
