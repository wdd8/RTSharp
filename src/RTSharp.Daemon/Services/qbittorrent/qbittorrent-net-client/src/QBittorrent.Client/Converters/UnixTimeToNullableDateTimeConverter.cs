using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class UnixTimeToNullableDateTimeConverter : JsonConverter<DateTime?>
    {
        public override void Write(Utf8JsonWriter writer, DateTime? value, JsonSerializerOptions options)
        {
            var dateTime = new DateTime(value!.Value.Ticks, DateTimeKind.Utc);
            var dateTimeOffset = new DateTimeOffset(dateTime, TimeSpan.Zero);
            writer.WriteNumberValue(dateTimeOffset.ToUnixTimeSeconds());
        }

        public override DateTime? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                var unixTime = reader.GetInt64();
                if (unixTime <= 0)
                    return null;

                var dto = DateTimeOffset.FromUnixTimeSeconds(unixTime);
                return new DateTime(dto.UtcTicks, DateTimeKind.Unspecified);
            }

            throw new JsonException($"Unexpected token {reader.TokenType} when parsing unix time.");
        }
    }
}
