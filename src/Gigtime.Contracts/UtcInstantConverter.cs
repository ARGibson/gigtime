using System.Text.Json;
using System.Text.Json.Serialization;

namespace Gigtime.Contracts;

/// <summary>Every wire instant is normalized to UTC, including offset-bearing input.</summary>
public sealed class UtcInstantConverter : JsonConverter<DateTimeOffset>
{
  public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	  => reader.GetDateTimeOffset().ToUniversalTime();
  public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
  {
	ArgumentNullException.ThrowIfNull(writer);
	writer.WriteStringValue(value.UtcDateTime);
  }
}
