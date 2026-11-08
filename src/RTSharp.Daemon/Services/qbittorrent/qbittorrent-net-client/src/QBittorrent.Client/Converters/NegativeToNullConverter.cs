using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    /// <summary>
    /// Reads negative numbers as <see langword="null"/>, and writes <see langword="null"/> as -1.
    /// </summary>
    internal class NegativeToNullConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(int?) || typeToConvert == typeof(long?);

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            typeToConvert == typeof(int?) ? new IntConverter() : new LongConverter();

        private sealed class IntConverter : JsonConverter<int?>
        {
            public override bool HandleNull => true;

            public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value ?? -1);

            public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;

                if (reader.TokenType == JsonTokenType.Number)
                {
                    var @int = reader.GetInt32();
                    return @int >= 0 ? @int : null;
                }

                throw new JsonException($"Unexpected token {reader.TokenType} when parsing integer.");
            }
        }

        private sealed class LongConverter : JsonConverter<long?>
        {
            public override bool HandleNull => true;

            public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(value ?? -1L);

            public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                if (reader.TokenType == JsonTokenType.Null)
                    return null;

                if (reader.TokenType == JsonTokenType.Number)
                {
                    var @long = reader.GetInt64();
                    return @long >= 0L ? @long : null;
                }

                throw new JsonException($"Unexpected token {reader.TokenType} when parsing integer.");
            }
        }
    }
}
