using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider;

using System.Text.RegularExpressions;

namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    internal static partial class Mapper
    {
        [GeneratedRegex("magnet:\\?xt=urn:[a-z0-9]+:[a-z0-9]{32,40}&dn=.+&tr=.+")]
        private static partial Regex MagnetDummyRegex();

        private static string TrackerStatusMessage(ReadOnlySpan<char> Raw)
        {
            if (Raw.Length == 1) {
                switch (Raw[0]) {
                    case '0': return "NA";
                    case '1': return "Completed";
                    case '2': return "Started";
                    case '3': return "Stopped";
                    case '4': return "Scrape";
                }
            }
            return Raw.ToString();
        }

        private static void ParseLabels(string Label, Google.Protobuf.Collections.RepeatedField<string> Labels)
        {
            var start = 0;
            for (var x = 0; x < Label.Length; x++) {
                if (Label[x] == ',' && (x == 0 || Label[x - 1] != '\\')) {
                    if (x > start)
                        Labels.Add(XMLUtils.Decode(Label[start..x]));
                    start = x + 1;
                }
            }
            if (start < Label.Length)
                Labels.Add(XMLUtils.Decode(Label[start..]));
        }

        public static TorrentTracker MapFromRaw(string Url, bool IsEnabled, bool IsUsable, uint Seeders, uint Peers, uint Downloaded, ulong LastUpdatedUnix, uint ScrapeInterval, string LatestEventRaw)
        {
            var tracker = new TorrentTracker();
            tracker.ID = tracker.Uri = XMLUtils.Decode(Url);
            TorrentTrackerStatus status;
            status = IsEnabled ? TorrentTrackerStatus.Enabled : TorrentTrackerStatus.Disabled;
            status |= IsUsable ? TorrentTrackerStatus.Active : TorrentTrackerStatus.NotActive;
            tracker.Status = status;
            tracker.Seeders = Seeders;
            tracker.Peers = Peers;
            tracker.Downloaded = Downloaded;
            tracker.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)LastUpdatedUnix));
            tracker.ScrapeInterval = Duration.FromTimeSpan(TimeSpan.FromSeconds(ScrapeInterval));
            tracker.StatusMessage = TrackerStatusMessage(LatestEventRaw);
            return tracker;
        }

        public static Torrent MapFromRaw(
            byte[] Hash, string NameRaw, ulong TotalSize, ulong Downloaded, ulong CompletedBytes, ulong Uploaded,
            ulong DownSpeed, ulong CreationDateRaw, ulong AddDateRaw, ulong FinishedDateRaw,
            string Label, string TorrentCommentRaw, bool IsMultiFile,
            string DirectoryDecoded, ulong PriorityRaw, ulong ChunkSize, bool IsComplete, ulong WantedChunks, ulong SkippedTotal,
            string StatusMessageDecoded, bool IsOpen, bool IsActive, bool IsHashChecking,
            bool IsPrivate, ulong SeedersConnected, ulong PeersConnected,
            IReadOnlyList<TorrentTracker> Trackers)
        {
            var priority = (TorrentPriority)PriorityRaw;

            var directory = DirectoryDecoded;
            if (IsMultiFile) {
                // If multifile remove d.name from path. Base path shouldn't point to any inner files of torrent
                var lastSep = directory.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
                directory = lastSep <= 0 ? "" : directory[..lastSep];
            }

            var wantedSize = IsComplete ? CompletedBytes : Math.Min(TotalSize, CompletedBytes + WantedChunks * ChunkSize);

            TorrentState torrentState;
            if (!IsOpen)
                torrentState = IsComplete ? TorrentState.Complete : TorrentState.Stopped;
            else {
                if (IsHashChecking)
                    torrentState = TorrentState.Hashing;
                else {
                    if (IsActive)
                        torrentState = IsComplete ? TorrentState.Seeding : TorrentState.Downloading;
                    else
                        torrentState = TorrentState.Paused;
                }
            }

            if (StatusMessageDecoded.Length != 0 && StatusMessageDecoded != "Tracker: [Tried all trackers.]")
                torrentState |= TorrentState.Errored;

            uint seedersTotal = 0, peersTotal = 0;
            foreach (var tracker in Trackers) {
                seedersTotal += tracker.Seeders;
                peersTotal += tracker.Peers;
            }

            var primaryTracker = PollingSubscription.GetPrimaryTracker(Trackers);

            var torrentComment = TorrentCommentRaw;
            if (torrentComment.StartsWith("VRS24mrker"))
                torrentComment = torrentComment[10..];

            var decodedName = XMLUtils.Decode(NameRaw);

            var torrent = new Torrent() {
                Hash = ByteString.CopyFrom(Hash),
                Name = decodedName,
                State = torrentState,
                WantedSize = wantedSize,
                IsPrivate = IsPrivate,
                Size = TotalSize,
                Downloaded = Downloaded,
                CompletedSize = CompletedBytes,
                Uploaded = Uploaded,
                DLSpeed = DownSpeed,
                CreatedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)CreationDateRaw)),
                AddedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)AddDateRaw)),
                FinishedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)FinishedDateRaw)),
                // label
                Comment = XMLUtils.Decode(torrentComment), // rutorrent compat
                RemotePath = directory,
                Priority = priority,
                ChunkSize = (uint)ChunkSize,
                Wasted = SkippedTotal,
                // trackers
                PrimaryTracker = primaryTracker,
                StatusMessage = StatusMessageDecoded,
                SeedersConnected = (uint)SeedersConnected,
                PeersConnected = (uint)PeersConnected,
                SeedersTotal = seedersTotal,
                PeersTotal = peersTotal,
                MagnetDummy = MagnetDummyRegex().IsMatch(decodedName),
                SequentialDownload = false, // rtorrent doesn't support sequential download??
            };

            ParseLabels(Label, torrent.Labels);

            return torrent;
        }
    }
}
