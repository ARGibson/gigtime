using System.Text.Json.Serialization;

namespace Gigtime.Contracts;

/// <summary>Closed generic response types keep the source-generated JSON graph AOT safe.</summary>
public sealed record Envelope<T>(bool Ok, string Command,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] T? Data = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] ErrorDto? Error = null) where T : class;

public sealed record ErrorDto(string Code, string Message, ErrorStateDto? State = null, string? Hint = null);
public sealed record ErrorStateDto(string? TaskId = null, string? State = null, string? Command = null);
public sealed record AcknowledgementDto(string? Id = null);
public sealed record HealthDto(string Status);

public static class Envelopes
{
  public static Envelope<T> Success<T>(string command, T data) where T : class => new(true, command, data);
  public static Envelope<T> Failure<T>(string command, ErrorDto error) where T : class => new(false, command, Error: error);
}
