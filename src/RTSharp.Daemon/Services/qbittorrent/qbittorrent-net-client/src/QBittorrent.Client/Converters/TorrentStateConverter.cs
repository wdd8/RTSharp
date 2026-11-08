using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace QBittorrent.Client.Converters;

internal class TorrentStateConverter : JsonConverter<TorrentState>
{
    private static readonly Dictionary<string, TorrentState> StringToTorrentState;
    private static readonly Dictionary<TorrentState, string> TorrentStateToString;

    static TorrentStateConverter()
    {
        TorrentStateToString = (
            from field in typeof(TorrentState).GetTypeInfo().DeclaredFields
            where field.IsStatic
            let attribute = field.GetCustomAttribute<EnumMemberAttribute>()
            select (value: (TorrentState) field.GetValue(null), strValue: attribute.Value)
        ).ToDictionary(t => t.value, t => t.strValue);

        StringToTorrentState = TorrentStateToString.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);
        StringToTorrentState.Add("stoppedUP", TorrentState.PausedUpload);
        StringToTorrentState.Add("stoppedDL", TorrentState.PausedDownload);
    }

    public override void Write(Utf8JsonWriter writer, TorrentState value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(TorrentStateToString.TryGetValue(value, out var s) ? s : value.ToString());
    }

    public override TorrentState Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException($"Unexpected token {reader.TokenType}.");

        var stringValue = reader.GetString()!;
        if (StringToTorrentState.TryGetValue(stringValue, out var torrentState))
            return torrentState;

        stringValue = stringValue.ToUpperInvariant();
        if (Enum.TryParse(stringValue, out torrentState))
            return torrentState;

        throw new JsonException($"Unexpected torrent state \"{stringValue}\".");
    }
}
