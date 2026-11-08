using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class SecondsToTimeSpanConverter : JsonConverter<TimeSpan?>
    {
        // null is written as -1
        public override bool HandleNull => true;

        public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value == null ? -1 : (long)value.Value.TotalSeconds);
        }

        public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number)
            {
                // Beyond long
                if (!reader.TryGetInt64(out var totalSeconds))
                    return TimeSpan.MaxValue;

                if (totalSeconds >= 0)
                {
                    return totalSeconds > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond
                        ? TimeSpan.MaxValue
                        : new TimeSpan(totalSeconds * TimeSpan.TicksPerSecond);
                }
                else
                {
                    return totalSeconds < TimeSpan.MinValue.Ticks / TimeSpan.TicksPerSecond
                        ? TimeSpan.MinValue
                        : new TimeSpan(totalSeconds * TimeSpan.TicksPerSecond);
                }
            }

            throw new JsonException($"Unexpected token {reader.TokenType} when parsing integer.");
        }
    }
}
