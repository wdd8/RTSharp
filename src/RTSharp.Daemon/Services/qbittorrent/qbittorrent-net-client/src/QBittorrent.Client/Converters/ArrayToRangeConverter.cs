using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class ArrayToRangeConverter : JsonConverter<Range>
    {
        public override void Write(Utf8JsonWriter writer, Range value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(value.StartIndex);
            writer.WriteNumberValue(value.EndIndex);
            writer.WriteEndArray();
        }

        public override Range Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException($"Unexpected token {reader.TokenType}.");

            long start = 0, end = 0;
            var count = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.Number)
                    throw new JsonException($"Unexpected token {reader.TokenType}.");

                var value = reader.GetInt64();
                switch (count++)
                {
                    case 0: start = value; break;
                    case 1: end = value; break;
                }
            }

            if (count != 2)
                throw new JsonException("Expected array of 2 elements.");
            return new Range(start, end);
        }
    }
}
