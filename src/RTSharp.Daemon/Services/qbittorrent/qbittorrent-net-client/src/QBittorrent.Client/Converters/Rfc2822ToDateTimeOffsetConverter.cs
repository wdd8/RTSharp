using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class Rfc2822ToDateTimeOffsetConverter : JsonConverter<DateTimeOffset?>
    {
        public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
        {
            var dto = value!.Value;
            // Note: RFC 2822 actually supports several formats, but it seems this one is enough for qBittorrent.
            var str = string.Format(CultureInfo.InvariantCulture,
                "{0:ddd, dd MMM yyyy HH:mm:ss} {1}{2:hhmm}",
                dto.DateTime,
                dto.Offset >= TimeSpan.Zero ? '+' : '-',
                dto.Offset);
            writer.WriteStringValue(str);
        }

        public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var stringValue = reader.GetString();
                if (string.IsNullOrWhiteSpace(stringValue))
                    return null;

                if (DateTimeOffset.TryParse(stringValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                    return dto;

                return null;
            }

            throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }
}
