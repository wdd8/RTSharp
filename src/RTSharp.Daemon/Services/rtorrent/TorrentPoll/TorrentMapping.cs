using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    internal static class Mapper
    {
        private static TorrentTracker? GetPrimaryTracker(IReadOnlyList<TorrentTracker>? Trackers)
        {
            if (Trackers == null)
                return null;

            TorrentTracker? best = null;
            foreach (var tracker in Trackers) {
                if (best == null || tracker.Peers > best.Peers ||
                    (tracker.Peers == best.Peers && tracker.Status.HasFlag(TorrentTrackerStatus.Working) && !best.Status.HasFlag(TorrentTrackerStatus.Working)))
                    best = tracker;
            }
            return best;
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

        public static TorrentTracker MapFromRaw(string Url, bool IsEnabled, bool IsBusy, bool IsRunning, uint Seeders, uint Peers, uint Downloaded,
            ulong SuccessTimeLastUnix, ulong FailedTimeLastUnix, uint NormalInterval, ulong ActivityTimeNextUnix)
        {
            var tracker = new TorrentTracker {
                Seeders = Seeders,
                Peers = Peers,
                Downloaded = Downloaded,
                StatusMessage = ""
            };
            tracker.ID = tracker.Uri = XMLUtils.Decode(Url);

            if (!IsEnabled) {
                tracker.Status = TorrentTrackerStatus.Disabled;
                return tracker;
            }

            if (!IsRunning || (SuccessTimeLastUnix == 0 && FailedTimeLastUnix == 0)) {
                tracker.Status = TorrentTrackerStatus.NotContactedYet;
            } else {
                tracker.Status = FailedTimeLastUnix > SuccessTimeLastUnix ? TorrentTrackerStatus.NotWorking : TorrentTrackerStatus.Working;
                tracker.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)Math.Max(SuccessTimeLastUnix, FailedTimeLastUnix)));
                if (SuccessTimeLastUnix != 0)
                    tracker.Interval = Duration.FromTimeSpan(TimeSpan.FromSeconds(NormalInterval));
            }

            if (!IsRunning)
                return tracker;

            if (IsBusy)
                tracker.Status |= TorrentTrackerStatus.Updating;
            else if (ActivityTimeNextUnix != 0)
                tracker.NextAnnounce = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)ActivityTimeNextUnix));

            return tracker;
        }

        public static Torrent MapFromRaw(
            ByteString Hash, string NameRaw, ulong TotalSize, ulong Downloaded, ulong CompletedBytes, ulong Uploaded,
            ulong DownSpeed, ulong CreationDateRaw, ulong AddDateRaw, ulong FinishedDateRaw,
            string Label, string TorrentCommentRaw, bool IsMultiFile,
            string DirectoryDecoded, ulong PriorityRaw, ulong ChunkSize, bool IsComplete, ulong WantedChunks, ulong SkippedTotal,
            string StatusMessageDecoded, bool IsOpen, bool IsActive, bool IsHashChecking,
            bool IsPrivate, ulong SeedersConnected, ulong PeersConnected, bool IsMeta,
            IReadOnlyList<TorrentTracker> Trackers)
        {
            var priority = (TorrentPriority)PriorityRaw;

            // d.directory keeps a trailing separator as it was set for single-file torrents
            var directory = System.IO.Path.TrimEndingDirectorySeparator(DirectoryDecoded);
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

            if (StatusMessageDecoded.Length != 0 && (IsPrivate || StatusMessageDecoded != "Tracker: [Tried all trackers.]"))
                torrentState |= TorrentState.Errored;

            uint seedersTotal = 0, peersTotal = 0;
            foreach (var tracker in Trackers) {
                seedersTotal += tracker.Seeders;
                peersTotal += tracker.Peers;
            }

            var primaryTracker = GetPrimaryTracker(Trackers);

            var torrentComment = TorrentCommentRaw;
            if (torrentComment.StartsWith("VRS24mrker"))
                torrentComment = torrentComment[10..];

            var decodedName = XMLUtils.Decode(NameRaw);

            if (IsMeta) {
                // for more consistent behavior across daemons
                ChunkSize = TotalSize = wantedSize = CompletedBytes = 0;
            }

            var torrent = new Torrent() {
                Hash = Hash,
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
                MagnetDummy = IsMeta,
                SequentialDownload = false, // rtorrent doesn't support sequential download??
            };

            ParseLabels(Label, torrent.Labels);

            return torrent;
        }
    }
}
