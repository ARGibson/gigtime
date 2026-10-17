namespace Gigtime.Contracts;

public static class Envelopes
{
	public static Envelope<T> Success<T>(string command, T data) where T : class => new(true, command, data);
	public static Envelope<T> Failure<T>(string command, ErrorDto error) where T : class => new(false, command, Error: error);
}
