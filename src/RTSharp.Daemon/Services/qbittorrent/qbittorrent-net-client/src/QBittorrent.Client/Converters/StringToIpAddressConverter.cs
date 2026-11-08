using System;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class StringToIpAddressConverter : JsonConverter<IPAddress>
    {
        public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }

        public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return IPAddress.Parse(reader.GetString());
            }

            throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }
}
