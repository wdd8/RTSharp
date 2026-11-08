using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class SaveLocationConverter : JsonConverter<SaveLocation>
    {
        public override void Write(Utf8JsonWriter writer, SaveLocation value, JsonSerializerOptions options)
        {
            if (value.StandardFolder != null)
            {
                writer.WriteNumberValue((int)value.StandardFolder.Value);
            }
            else if (!string.IsNullOrWhiteSpace(value.CustomFolder))
            {
                writer.WriteStringValue(value.CustomFolder);
            }
            else
            {
                throw new JsonException("Invalid save location object.");
            }
        }

        public override SaveLocation Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return new SaveLocation((StandardSaveLocation)reader.GetInt32());
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                return new SaveLocation(reader.GetString());
            }

            throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }
}
