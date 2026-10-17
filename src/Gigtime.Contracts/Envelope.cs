using System.Text.Json.Serialization;

namespace Gigtime.Contracts;

/// <summary>Closed generic response types keep the source-generated JSON graph AOT safe.</summary>
public sealed record Envelope<T>(
	bool Ok,
	string Command,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	T? Data = null,
	[property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	ErrorDto? Error = null) where T : class;

public sealed record ErrorDto(string Code, string Message, ErrorStateDto? State = null, string? Hint = null);

public sealed record ErrorStateDto(string? TaskId = null, string? State = null, string? Command = null);
