using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    internal class SeparatedStringToListConverter<TList> : JsonConverter<TList> where TList : class, IEnumerable<string>
    {
        private readonly string[] _separators;

        public SeparatedStringToListConverter(params string[] separators)
        {
            if (separators == null)
                throw new ArgumentNullException(nameof(separators));
            if (!separators.Any())
                throw new ArgumentException("At least one separator must be provided", nameof(separators));
            if (separators.Any(string.IsNullOrEmpty))
                throw new ArgumentException("A separator cannot be an empty string.", nameof(separators));

            _separators = separators.ToArray();
        }

        public override void Write(Utf8JsonWriter writer, TList value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(string.Join(_separators[0], value));
        }

        public override TList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return (TList)(object)reader.GetString().Split(_separators, StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            throw new JsonException($"Unexpected token {reader.TokenType}.");
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    internal sealed class SeparatedStringToListAttribute(params string[] separators) : JsonConverterAttribute
    {
        public override JsonConverter CreateConverter(Type typeToConvert) =>
            (JsonConverter)Activator.CreateInstance(typeof(SeparatedStringToListConverter<>).MakeGenericType(typeToConvert), [separators]);
    }
}
