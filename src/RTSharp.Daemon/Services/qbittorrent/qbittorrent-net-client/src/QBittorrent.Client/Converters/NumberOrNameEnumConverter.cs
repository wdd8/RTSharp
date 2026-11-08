using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters
{
    /// <summary>
    /// Reads enums from numbers or names and writes them as numbers
    /// </summary>
    internal sealed class NumberOrNameEnumConverter : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(Converter<>).MakeGenericType(typeToConvert));

        private sealed class Converter<T> : JsonConverter<T> where T : struct, Enum
        {
            private static readonly Dictionary<string, T> Names = GetNames();

            private static Dictionary<string, T> GetNames()
            {
                var names = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
                foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    var value = (T)field.GetValue(null);
                    names.TryAdd(field.Name, value);
                    if (field.GetCustomAttribute<EnumMemberAttribute>()?.Value is { } memberName)
                        names.TryAdd(memberName, value);
                }
                return names;
            }

            public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.Number:
                        return (T)Enum.ToObject(typeof(T), reader.GetInt64());
                    case JsonTokenType.String:
                        var name = reader.GetString();
                        // Enum.TryParse also takes numbers given as strings, and combined flags
                        if (Names.TryGetValue(name, out var value) || Enum.TryParse(name, true, out value))
                            return value;
                        throw new JsonException($"Unexpected {typeof(T).Name} value \"{name}\".");
                    default:
                        throw new JsonException($"Unexpected token {reader.TokenType}.");
                }
            }

            public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
                writer.WriteNumberValue(Convert.ToInt64(value));
        }
    }
}
