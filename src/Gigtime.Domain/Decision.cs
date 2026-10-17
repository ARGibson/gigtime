namespace Gigtime.Domain;

/// <summary>Deciders produce events without mutating the state they were given.</summary>
public sealed record Decision(IReadOnlyList<object> Events, Rejection? Error = null)
{
	public bool IsSuccess => Error is null;
	public static Decision Accept(params object[] events) => new(events);

	public static Decision Refuse(string code, string message, string state, string command, string? hint = null)
	{
		return new([], new(code, message, state, command, hint));
	}
}

/// <summary>A refusal carries enough state to explain the next useful command.</summary>
public sealed record Rejection(string Code, string Message, string State, string Command, string? Hint = null);
