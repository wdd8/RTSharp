using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    /// <summary>
    /// Reads RSS dates in the RFC 822 format with fallback
    /// </summary>
    internal sealed class RssDateTimeOffsetConverter : JsonConverterFactory
    {
        private const string Format = "ddd, dd MMM yyyy HH:mm:ss zzz";

        public override bool CanConvert(Type typeToConvert) =>
            typeToConvert == typeof(DateTimeOffset) || typeToConvert == typeof(DateTimeOffset?);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            typeToConvert == typeof(DateTimeOffset) ? new DateConverter() : new NullableDateConverter();

        private static string ReadString(ref Utf8JsonReader reader)
        {
            if (reader.TokenType != JsonTokenType.String)
                throw new JsonException($"Unexpected token {reader.TokenType}.");
            return reader.GetString();
        }

        private static DateTimeOffset Parse(string value)
        {
            if (DateTimeOffset.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date;

            throw new JsonException($"Unexpected date \"{value}\".");
        }

        private sealed class DateConverter : JsonConverter<DateTimeOffset>
        {
            public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
                Parse(ReadString(ref reader));

            public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
        }

        private sealed class NullableDateConverter : JsonConverter<DateTimeOffset?>
        {
            public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                var value = ReadString(ref reader);
                return string.IsNullOrWhiteSpace(value) ? null : Parse(value);
            }

            public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options) =>
                writer.WriteStringValue(value!.Value.ToString(Format, CultureInfo.InvariantCulture));
        }
    }
}
