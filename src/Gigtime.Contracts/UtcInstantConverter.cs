using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gigtime.Contracts;

/// <summary>Every wire instant is normalized to UTC, including offset-bearing input.</summary>
public sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
	public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType != JsonTokenType.String) throw new JsonException("An instant must be a JSON string with an explicit offset.");

		var text = reader.GetString()!;
		// #1: reject offset-free wire instants before the framework can assume host-local time.
		var timeStart = text.IndexOf('T');

		if (timeStart < 0 || !(text.EndsWith('Z') || text.AsSpan(timeStart).Contains('+') || text.AsSpan(timeStart).Contains('-')))
			throw new JsonException("An instant requires Z or an explicit UTC offset.");

		return reader.GetDateTimeOffset().ToUniversalTime();
	}

	public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
	{
		ArgumentNullException.ThrowIfNull(writer);
		writer.WriteStringValue(value.UtcDateTime);
	}
}
