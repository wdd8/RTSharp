using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal sealed class NullableUriConverter : JsonConverter<Uri>
    {
        public override Uri Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String && Uri.TryCreate(reader.GetString(), UriKind.RelativeOrAbsolute, out var uri))
                return uri;

            reader.Skip();
            return null;
        }

        public override void Write(Utf8JsonWriter writer, Uri value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.OriginalString);
    }
}
