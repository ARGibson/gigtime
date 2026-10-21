namespace Gigtime.Contracts;

public static class Envelopes
{
	public static Envelope<T> Success<T>(string command, T data, IReadOnlyList<WarningDto>? warnings = null) where T : class =>
		new(true, command, data, Warnings: warnings is { Count: > 0 } ? warnings : null);

	public static Envelope<T> Failure<T>(string command, ErrorDto error) where T : class => new(false, command, Error: error);
}
