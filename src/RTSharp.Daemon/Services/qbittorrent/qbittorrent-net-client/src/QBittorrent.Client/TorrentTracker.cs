using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using QBittorrent.Client.Converters;

namespace QBittorrent.Client
{
    /// <summary>
    /// Represents the torrent tracker information.
    /// </summary>
    public class TorrentTracker
    {
        private static readonly IDictionary<string, TorrentTrackerStatus> StatusMap = 
            new Dictionary<string, TorrentTrackerStatus>(StringComparer.OrdinalIgnoreCase)
            {
                ["Disabled"] = TorrentTrackerStatus.Disabled,
                ["Working"] = TorrentTrackerStatus.Working,
                ["Updating..."] = TorrentTrackerStatus.Updating,
                ["Not working"] = TorrentTrackerStatus.NotWorking,
                ["Not contacted yet"] = TorrentTrackerStatus.NotContacted
            };

        /// <summary>
        /// Tracker URL
        /// </summary>
        [JsonPropertyName("url")]
        public Uri Url { get; set; }

        /// <summary>
        /// Tracker status as string
        /// </summary>
        /// <seealso cref="TrackerStatus"/>
        /// <remarks>
        /// <para>Until API v2.2.0 this property might return localized status string.</para>
        /// <para>Starting from API v2.2.0 this property always returns English status string.</para>
        /// </remarks>
        [JsonIgnore]
        public string Status { get; set; }

        /// <summary>
        /// Tracker status
        /// </summary>
        /// <seealso cref="Status"/>
        /// <remarks>
        /// Until API v2.2.0 this property might always return null on non-English localizations.
        /// </remarks>
        [JsonIgnore]
        public TorrentTrackerStatus? TrackerStatus { get; set; }

        /// <summary>
        /// Number of peers for current torrent reported by the tracker
        /// </summary>
        [JsonPropertyName("num_peers")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        public int? Peers { get; set; }

        /// <summary>
        /// Number of seeds for current torrent reported by the tracker
        /// </summary>
        [JsonPropertyName("num_seeds")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        [ApiLevel(ApiLevel.V2, MinVersion = "2.2.0")]
        public int? Seeds { get; set; }

        /// <summary>
        /// Number of leeches for current torrent reported by the tracker
        /// </summary>
        [JsonPropertyName("num_leeches")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        [ApiLevel(ApiLevel.V2, MinVersion = "2.2.0")]
        public int? Leeches { get; set; }

        /// <summary>
        /// Number of completed downloads for current torrent reported by the tracker
        /// </summary>
        [JsonPropertyName("num_downloaded")]
        [JsonConverter(typeof(NegativeToNullConverter))]
        [ApiLevel(ApiLevel.V2, MinVersion = "2.2.0")]
        public int? CompletedDownloads { get; set; }

        /// <summary>
        /// Tracker priority tier. Lower tier trackers are tried before higher tiers
        /// </summary>
        [JsonPropertyName("tier")]
        [ApiLevel(ApiLevel.V2, MinVersion = "2.2.0")]
        public int? Tier { get; set; }

        /// <summary>
        /// Tracker message (there is no way of knowing what this message is - it's up to tracker admins)
        /// </summary>
        [JsonPropertyName("msg")]
        public string Message { get; set; }

        [JsonPropertyName("status")]
        [JsonInclude]
        [JsonConverter(typeof(StatusConverter))]
        private StatusJson StatusField
        {
            get => new(Status, TrackerStatus);
            set => (Status, TrackerStatus) = (value.Text, value.Status);
        }

        private readonly record struct StatusJson(string Text, TorrentTrackerStatus? Status);

        /// <summary>
        /// Reads the status from a string before API v2.2.0, which may be localized, and from a number since.
        /// </summary>
        private sealed class StatusConverter : JsonConverter<StatusJson>
        {
            public override StatusJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.String:
                        var text = reader.GetString();
                        return new StatusJson(text, StringToStatus(text));
                    case JsonTokenType.Number:
                        var status = (TorrentTrackerStatus)reader.GetInt32();
                        return new StatusJson(StatusToString(status), status);
                    case JsonTokenType.Null:
                        return default;
                    default:
                        throw new JsonException($"Unexpected token {reader.TokenType}.");
                }
            }

            public override void Write(Utf8JsonWriter writer, StatusJson value, JsonSerializerOptions options)
            {
                if (value.Status != null)
                    writer.WriteNumberValue((int)value.Status.Value);
                else if (value.Text != null)
                    writer.WriteStringValue(value.Text);
                else
                    writer.WriteNullValue();
            }
        }

        private static string StatusToString(TorrentTrackerStatus? status)
        {
            return StatusMap.FirstOrDefault(x => x.Value == status).Key;
        }

        private static TorrentTrackerStatus? StringToStatus(string statusString)
        {
            return StatusMap.TryGetValue(statusString, out var status) ? status : (TorrentTrackerStatus?)null;
        }
    }
}
