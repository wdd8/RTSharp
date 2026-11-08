using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class MinutesToTimeSpanConverter : JsonConverter<TimeSpan?>
    {
        // null is written as -1
        public override bool HandleNull => true;

        public override void Write(Utf8JsonWriter writer, TimeSpan? value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(value == null ? -1 : (long)value.Value.TotalMinutes);
        }

        public override TimeSpan? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
                return null;

            if (reader.TokenType == JsonTokenType.Number)
            {
                // Beyond long
                if (!reader.TryGetInt64(out var totalMinutes))
                    return TimeSpan.MaxValue;

                if (totalMinutes >= 0)
                {
                    return totalMinutes > TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerMinute
                        ? TimeSpan.MaxValue
                        : new TimeSpan(totalMinutes * TimeSpan.TicksPerMinute);
                }
                else
                {
                    return totalMinutes < TimeSpan.MinValue.Ticks / TimeSpan.TicksPerMinute
                        ? TimeSpan.MinValue
                        : new TimeSpan(totalMinutes * TimeSpan.TicksPerMinute);
                }
            }

            throw new JsonException($"Unexpected token {reader.TokenType} when parsing integer.");
        }
    }
}
