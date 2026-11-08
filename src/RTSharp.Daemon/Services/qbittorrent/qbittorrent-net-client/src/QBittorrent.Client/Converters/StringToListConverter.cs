using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class StringToListConverter<TList> : JsonConverter<TList> where TList : class, IEnumerable<string>
    {
        private readonly string _separator;
        private readonly bool _trim;

        public StringToListConverter(string separator, bool trim)
        {
            _separator = separator;
            _trim = trim;
        }

        public override void Write(Utf8JsonWriter writer, TList value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(string.Join(_separator, value));
        }

        public override TList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var list = reader.GetString().Split(_separator, StringSplitOptions.RemoveEmptyEntries);
                if (_trim)
                {
                    list = list.Select(x => x.Trim()).ToArray();
                }

                return (TList)(object)list;
            }

            throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }

    /// <summary>
    /// Applies <see cref="StringToListConverter{TList}"/> with the given separator.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class StringToListAttribute(string separator = "\n", bool trim = false) : JsonConverterAttribute
    {
        public override JsonConverter CreateConverter(Type typeToConvert) =>
            (JsonConverter)Activator.CreateInstance(typeof(StringToListConverter<>).MakeGenericType(typeToConvert), separator, trim);
    }
}
