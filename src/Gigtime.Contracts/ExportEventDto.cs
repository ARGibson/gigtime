using System.Text.Json;

namespace Gigtime.Contracts;

public sealed record ExportEventDto(
	long Sequence,
	string Type,
	string Stream,
	long Version,
	DateTimeOffset Timestamp,
	string? CorrelationId,
	JsonElement Data);
