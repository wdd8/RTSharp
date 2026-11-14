using RTSharp.Daemon.Services.TorrentPoll;
using BencodeNET.Objects;
using BencodeNET.Parsing;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Utils;

using System.Buffers;
using System.Diagnostics;

using SpeedMovingAverage = RTSharp.Shared.Abstractions.SpeedMovingAverage;

namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public partial class TorrentPolling : TorrentPollingService
    {
        protected internal override DeltaTorrentsListResponse BuildChanges(HistoryEntry? Previous, HistoryEntry Current)
        {
            var delta = new DeltaTorrentsListResponse();
            if (Previous != null && ReferenceEquals(Previous.List, Current.List))
                return delta;

            foreach (var (hash, torrent) in Current.List) {
                if (Previous == null || !Previous.List.TryGetValue(hash, out var old)) {
                    delta.FullUpdate.Add(torrent);
                    continue;
                }

                Current.Trackers.TryGetValue(hash, out var trackers);
                Previous.Trackers.TryGetValue(hash, out var oldTrackers);

                if (ReferenceEquals(torrent, old) && ReferenceEquals(trackers, oldTrackers))
                    continue;

                if (torrent.Name != old.Name ||
                    torrent.Size != old.Size ||
                    torrent.IsCompleted != old.IsCompleted ||
                    torrent.IsPrivate != old.IsPrivate ||
                    torrent.ChunkSize != old.ChunkSize ||
                    torrent.State != old.State ||
                    torrent.WantedSize != old.WantedSize ||
                    torrent.Priority != old.Priority ||
                    torrent.MagnetDummy != old.MagnetDummy ||
                    torrent.SequentialDownload != old.SequentialDownload ||
                    torrent.RemotePath != old.RemotePath ||
                    torrent.StatusMessage != old.StatusMessage ||
                    !Equals(torrent.FinishedOn, old.FinishedOn) ||
                    !torrent.Labels.Equals(old.Labels) ||
                    TrackersChanged(trackers, oldTrackers) ||
                    torrent.PrimaryTracker?.Uri != old.PrimaryTracker?.Uri) {
                    delta.FullUpdate.Add(torrent);
                } else if (torrent.IsCompleted) {
                    if (torrent.Uploaded != old.Uploaded ||
                        torrent.UPSpeed != old.UPSpeed ||
                        torrent.SeedersTotal != old.SeedersTotal ||
                        torrent.PeersTotal != old.PeersTotal ||
                        torrent.PeersConnected != old.PeersConnected)
                        delta.Complete.Add(TorrentDeltaMessages.Complete(torrent));
                } else if (torrent.Uploaded != old.Uploaded ||
                    torrent.Downloaded != old.Downloaded ||
                    torrent.CompletedSize != old.CompletedSize ||
                    torrent.DLSpeed != old.DLSpeed ||
                    torrent.IOSpeed != old.IOSpeed ||
                    torrent.UPSpeed != old.UPSpeed ||
                    torrent.SeedersConnected != old.SeedersConnected ||
                    torrent.SeedersTotal != old.SeedersTotal ||
                    torrent.PeersConnected != old.PeersConnected ||
                    torrent.PeersTotal != old.PeersTotal ||
                    torrent.Wasted != old.Wasted ||
                    !Equals(torrent.ETA, old.ETA)) {
                    delta.Incomplete.Add(TorrentDeltaMessages.Incomplete(torrent));
                }
            }

            if (Previous != null) {
                foreach (var (hash, torrent) in Previous.List) {
                    if (!Current.List.ContainsKey(hash))
                        delta.Removed.Add(torrent.Hash);
                }
            }
            return delta;
        }

        private static bool TrackersChanged(TorrentsTrackersReply.Types.TorrentsTrackers? Current,
            TorrentsTrackersReply.Types.TorrentsTrackers? Previous)
        {
            if (ReferenceEquals(Current, Previous))
                return false;
            if (Current == null || Previous == null)
                return true;
            if (Current.Trackers.Count != Previous.Trackers.Count)
                return true;

            for (var x = 0; x < Current.Trackers.Count; x++) {
                var tracker = Current.Trackers[x];
                // Usually in the same order as the previous poll
                var old = Previous.Trackers[x];
                if (old.Uri != tracker.Uri) {
                    old = null;
                    foreach (var candidate in Previous.Trackers) {
                        if (candidate.Uri == tracker.Uri) {
                            old = candidate;
                            break;
                        }
                    }
                }
                if (old == null || tracker.Status != old.Status ||
                    tracker.StatusMessage != old.StatusMessage || tracker.LastUpdated != old.LastUpdated)
                    return true;
            }
            return false;
        }

        private string InstanceKey { get; }
        private IServiceScopeFactory ScopeFactory { get; }
        private V1ToV2InfoHashCache Cache { get; }
        private ILogger<TorrentPolling> Logger { get; }

        public TorrentPolling(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, V1ToV2InfoHashCache Cache, ILogger<TorrentPolling> Logger, EventsService Events) : base(Logger, Events)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
            this.Cache = Cache;
            this.Logger = Logger;
        }

        private readonly InfoHashDictionary<(int Attempts, DateTime NextTry)> ResolveBackoff = new();

        private const int SPEED_HISTORY_MAX = 5;

        private sealed class PolledTorrent
        {
            public byte[] Raw = [];
            public byte[]? NativeHash;
            public string TrackersRaw = "";
            public bool TrackersRunning;
            public TorrentsTrackersReply.Types.TorrentsTrackers? Trackers;
            public Torrent? Torrent;
            public long LastPoll;

            public readonly (long Poll, DateTime Date, ulong Uploaded, ulong CompletedSize)[] Samples = new (long, DateTime, ulong, ulong)[SPEED_HISTORY_MAX];
            public int Count;

            public void AddSample(long Poll, DateTime Date, ulong Uploaded, ulong CompletedSize)
            {
                if (Count == Samples.Length) {
                    Array.Copy(Samples, 1, Samples, 0, Count - 1);
                    Count--;
                }
                Samples[Count++] = (Poll, Date, Uploaded, CompletedSize);
            }

            /// <param name="OldestPoll">Samples from polls before this one are out of the window</param>
            public double Speed(long OldestPoll, bool Uploaded)
            {
                Span<(DateTime, long)> buf = stackalloc (DateTime, long)[SPEED_HISTORY_MAX];
                var c = 0;
                for (var x = 0; x < Count; x++) {
                    var sample = Samples[x];
                    if (sample.Poll >= OldestPoll)
                        buf[c++] = (sample.Date, (long)(Uploaded ? sample.Uploaded : sample.CompletedSize));
                }
                return SpeedMovingAverage.CalculateSpeed(buf[..c]);
            }
        }

        private readonly InfoHashDictionary<PolledTorrent> Polled = new();
        private long PollCount;
        private readonly List<(byte[] Key, Torrent Torrent, TorrentsTrackersReply.Types.TorrentsTrackers Trackers)> PollEntries = [];
        private HistoryEntry? LastSnapshot;

        private static string AllTorrentsXml(bool IsV16) => IsV16 ? AllTorrentsXmlV16 : AllTorrentsXmlOld;

        private static readonly string AllTorrentsXmlOld = BuildAllTorrentsXml("d.multicall2");
        private static readonly string AllTorrentsXmlV16 = BuildAllTorrentsXml("d.multicall");

        private static string BuildAllTorrentsXml(string MulticallMethod) =>
                "<?xml version=\"1.0\" ?><methodCall><methodName>" + MulticallMethod + "</methodName><params><param><value><string></string></value></param><param><value><string>main</string></value></param><param><value><string>"
                + "d.hash=</string></value></param><param><value><string>"
                + "d.name=</string></value></param><param><value><string>"
                + "d.size_bytes=</string></value></param><param><value><string>"
                + "d.completed_bytes=</string></value></param><param><value><string>"
                + "d.down.total=</string></value></param><param><value><string>"
                + "d.up.total=</string></value></param><param><value><string>"
                + "d.down.rate=</string></value></param><param><value><string>"
                + "d.creation_date=</string></value></param><param><value><string>"
                + "d.custom=addtime</string></value></param><param><value><string>"
                + "d.timestamp.finished=</string></value></param><param><value><string>"
                + "d.custom1=</string></value></param><param><value><string>" // label
                + "d.custom2=</string></value></param><param><value><string>" // torrent comment
                + "d.is_multi_file=</string></value></param><param><value><string>"
                + "d.directory=</string></value></param><param><value><string>"
                + "d.priority=</string></value></param><param><value><string>"
                + "d.chunk_size=</string></value></param><param><value><string>"
                + "d.complete=</string></value></param><param><value><string>"
                + "d.wanted_chunks=</string></value></param><param><value><string>"
                + "d.skip.total=</string></value></param><param><value><string>"
                + "cat=\"$t.multicall=d.hash=,t.url=,cat=^,t.is_enabled=,cat=^,t.is_busy=,cat=^,t.scrape_complete=,cat=^,t.scrape_incomplete=,cat=^,t.scrape_downloaded=,cat=^,t.success_time_last=,cat=^,t.failed_time_last=,cat=^,t.normal_interval=,cat=^,t.activity_time_next=,cat=|\""
                + "</string></value></param><param><value><string>"
                + "d.message=</string></value></param><param><value><string>"
                + "d.is_open=</string></value></param><param><value><string>"
                + "d.is_active=</string></value></param><param><value><string>"
                + "d.is_hash_checking=</string></value></param><param><value><string>"
                + "d.is_private=</string></value></param><param><value><string>"
                + "d.peers_complete=</string></value></param><param><value><string>"
                + "d.peers_accounted=</string></value></param><param><value><string>"
                + "d.is_meta=</string></value></param>"
                + "</params></methodCall>";

        private async Task<SCGICommunication.ScgiResponse> FetchAllTorrents(CancellationToken CancellationToken)
        {
            using var scope = ScopeFactory.CreateScope();
            var comm = scope.ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            var version = scope.ServiceProvider.GetRequiredKeyedService<RtorrentVersionService>(InstanceKey);

            return await comm.Get(AllTorrentsXml(await version.IsV16()), CancellationToken);
        }

        /// <summary>
        /// Reads the next torrent's entry of the d.multicall response
        /// </summary>
        private static bool NextEntry(ref SequenceReader<byte> Xml, out ReadOnlySequence<byte> Entry)
        {
            if (XMLUtils.CheckFor(Xml, XMLUtils.DATA_TOKEN_END)) {
                Debug.Assert(Xml.Remaining == 31 + 2 + 9 + 2 + 17 + 2);
                Entry = default;
                return false;
            }

            XMLUtils.SeekFixed(ref Xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

            Debug.Assert(Xml.Sequence.IsSingleSegment);
            var length = Xml.UnreadSpan.IndexOf(XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            if (length < 0)
                throw new InvalidDataException($"Malformed response at pos {Xml.Consumed}");

            Entry = Xml.Sequence.Slice(Xml.Position, length);
            Xml.Advance(length + XMLUtils.MULTICALL_RESPONSE_ENTRY_END.Length);
            return true;
        }

        private byte[] KeyOf(byte[] Hash, bool IsMeta, List<byte[]> UnknownV1s)
        {
            if (Cache.TryGetRTSHash(Hash, out var rtsHash))
                return rtsHash;
            if (!IsMeta && (!ResolveBackoff.TryGetValue(Hash, out var backoff) || DateTime.UtcNow >= backoff.NextTry))
                UnknownV1s.Add(Hash);
            return Hash;
        }

        /// <summary>
        /// Parses a torrent's entry of the d.multicall response
        /// </summary>
        /// <param name="Previous">What the poll loop kept of the torrent</param>
        private (byte[] Hash, byte[] Key, Torrent Torrent, string TrackersRaw, bool TrackersRunning, TorrentsTrackersReply.Types.TorrentsTrackers Trackers) ParseEntry(SequenceReader<byte> Entry, PolledTorrent? Previous, List<byte[]> UnknownV1s)
        {
            var hashRaw = XMLUtils.GetValue<string>(ref Entry);
            var name = XMLUtils.GetValue<string>(ref Entry);
            var totalSize = XMLUtils.GetValue<ulong>(ref Entry);
            var completedBytes = XMLUtils.GetValue<ulong>(ref Entry);
            var downloaded = XMLUtils.GetValue<ulong>(ref Entry);
            var uploaded = XMLUtils.GetValue<ulong>(ref Entry);

            var downSpeed = XMLUtils.GetValue<ulong>(ref Entry);

            var creationDateRaw = XMLUtils.GetValue<ulong>(ref Entry);
            if (!UInt64.TryParse(XMLUtils.GetValue<string>(ref Entry), out var addDateRaw)) {
                addDateRaw = 0;
            }
            var finishedDateRaw = XMLUtils.GetValue<ulong>(ref Entry);

            var label = XMLUtils.GetValue<string>(ref Entry);
            var torrentComment = XMLUtils.GetValue<string>(ref Entry);
            var isMultiFile = XMLUtils.GetValue<bool>(ref Entry);
            var directory = XMLUtils.Decode(XMLUtils.GetValue<string>(ref Entry));
            var priorityRaw = XMLUtils.GetValue<ulong>(ref Entry);
            var chunkSize = XMLUtils.GetValue<ulong>(ref Entry);
            var isComplete = XMLUtils.GetValue<bool>(ref Entry);
            var wantedChunks = XMLUtils.GetValue<ulong>(ref Entry);
            var skippedTotal = XMLUtils.GetValue<ulong>(ref Entry);

            var trackersRaw = XMLUtils.GetValue<string>(ref Entry);

            var statusMessage = XMLUtils.Decode(XMLUtils.GetValue<string>(ref Entry));

            var isOpenRaw = XMLUtils.GetValue<bool>(ref Entry);
            var isActiveRaw = XMLUtils.GetValue<bool>(ref Entry);
            var isHashCheckingRaw = XMLUtils.GetValue<bool>(ref Entry);
            var isPrivateRaw = XMLUtils.GetValue<bool>(ref Entry);

            var seedersConnected = XMLUtils.GetValue<ulong>(ref Entry);
            var peersConnected = XMLUtils.GetValue<ulong>(ref Entry);
            var isMeta = XMLUtils.GetValue<bool>(ref Entry);

            var hash = Previous?.NativeHash ?? Convert.FromHexString(hashRaw);
            var key = KeyOf(hash, isMeta, UnknownV1s);

            var trackersRunning = isOpenRaw && isActiveRaw;

            // Trackers rarely change between polls
            TorrentsTrackersReply.Types.TorrentsTrackers torrentTrackers;
            if (Previous is { Trackers: { } previousTrackers } && Previous.TrackersRaw == trackersRaw && Previous.TrackersRunning == trackersRunning && previousTrackers.InfoHash.Span.SequenceEqual(key)) {
                torrentTrackers = previousTrackers;
            } else {
                torrentTrackers = new TorrentsTrackersReply.Types.TorrentsTrackers {
                    InfoHash = ByteString.CopyFrom(key)
                };

                Span<Range> trackerRanges = stackalloc Range[64];
                Span<Range> trackerPartRanges = stackalloc Range[10];

                var trackersRawSpan = trackersRaw.AsSpan();
                var trackerCount = trackersRawSpan.Split(trackerRanges, '|', StringSplitOptions.RemoveEmptyEntries);

                for (var x = 0; x < trackerCount; x++) {
                    var trackerRaw = trackersRawSpan[trackerRanges[x]];
                    var partCount = trackerRaw.Split(trackerPartRanges, '^', StringSplitOptions.RemoveEmptyEntries);
                    Debug.Assert(partCount == 10);

                    torrentTrackers.Trackers.Add(Mapper.MapFromRaw(
                        trackerRaw[trackerPartRanges[0]].ToString(),
                        trackerRaw[trackerPartRanges[1]] is "1",
                        trackerRaw[trackerPartRanges[2]] is "1",
                        trackersRunning,
                        UInt32.Parse(trackerRaw[trackerPartRanges[3]]),
                        UInt32.Parse(trackerRaw[trackerPartRanges[4]]),
                        UInt32.Parse(trackerRaw[trackerPartRanges[5]]),
                        UInt64.Parse(trackerRaw[trackerPartRanges[6]]),
                        UInt64.Parse(trackerRaw[trackerPartRanges[7]]),
                        UInt32.Parse(trackerRaw[trackerPartRanges[8]]),
                        UInt64.Parse(trackerRaw[trackerPartRanges[9]])));
                }
            }

            var torrent = Mapper.MapFromRaw(
                torrentTrackers.InfoHash, name, totalSize, downloaded, completedBytes, uploaded, downSpeed,
                creationDateRaw, addDateRaw, finishedDateRaw, label, torrentComment,
                isMultiFile, directory, priorityRaw, chunkSize, isComplete, wantedChunks, skippedTotal,
                statusMessage, isOpenRaw, isActiveRaw, isHashCheckingRaw, isPrivateRaw,
                seedersConnected, peersConnected, isMeta, torrentTrackers.Trackers);

            return (hash, key, torrent, trackersRaw, trackersRunning, torrentTrackers);
        }

        public async Task<(InfoHashDictionary<Torrent> List, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers)> GetAllTorrents(CancellationToken CancellationToken = default)
        {
            using var response = await FetchAllTorrents(CancellationToken);

            var torrents = new InfoHashDictionary<Torrent>();
            var trackers = new InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>();
            var unknownV1s = new List<byte[]>();

            var xml = response.Reader();
            XMLUtils.SeekTo(ref xml, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_START);

            while (NextEntry(ref xml, out var entry)) {
                var (_, key, torrent, _, _, torrentTrackers) = ParseEntry(new SequenceReader<byte>(entry), null, unknownV1s);
                trackers[key] = torrentTrackers;
                torrents[key] = torrent;
            }

            if (unknownV1s.Count > 0)
                await ResolveUnknownHashes(unknownV1s, torrents, trackers);

            return (torrents, trackers);
        }

        /// <param name="UnknownV1s">From <see cref="KeyOf"/>, so only ones due to resolve</param>
        private async Task ResolveUnknownHashes(List<byte[]> UnknownV1s, InfoHashDictionary<Torrent> Torrents, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers)
        {
            var now = DateTime.UtcNow;
            // The cache can have learned some since
            var toResolve = UnknownV1s.Where(v1 => !Cache.Contains(v1)).ToList();

            if (toResolve.Count == 0)
                return;

            void recordResolveBackoff(byte[] V1, DateTime Now)
            {
                var attempts = ResolveBackoff.TryGetValue(V1, out var b) ? b.Attempts + 1 : 1;
                var delaySeconds = Math.Min(300, Math.Pow(2, Math.Min(attempts, 8)));
                ResolveBackoff[V1] = (attempts, Now.AddSeconds(delaySeconds));
            }

            InfoHashDictionary<string> paths;
            using (var scope = ScopeFactory.CreateScope()) {
                var op = scope.ServiceProvider.GetRequiredKeyedService<RTorrentOpService>(InstanceKey);
                try {
                    paths = await op.GetDotTorrentFilePaths(toResolve);
                } catch (Exception ex) {
                    Logger.LogWarning(ex, "Failed to resolve session paths for {Count} unknown torrent(s)", toResolve.Count);
                    foreach (var v1 in toResolve)
                        recordResolveBackoff(v1, now);
                    return;
                }
            }

            var parser = new BencodeParser();

            foreach (var v1 in toResolve) {
                try {
                    var bytes = await File.ReadAllBytesAsync(paths[v1]);
                    using var ms = new MemoryStream(bytes);
                    var dict = parser.Parse<BDictionary>(ms);

                    var (_, v2) = BencodeUtils.GetInfoHashes(dict);
                    Cache.Add(v1, v2);
                    ResolveBackoff.Remove(v1);

                    if (v2 != null) {
                        var v2Hash = ByteString.CopyFrom(v2);
                        if (Torrents.Remove(v1, out var t)) {
                            var moved = t.Clone();
                            moved.Hash = v2Hash;
                            Torrents[v2] = moved;
                        }
                        if (Trackers.Remove(v1, out var tr))
                            Trackers[v2] = new() { InfoHash = v2Hash, Trackers = { tr.Trackers } };
                    }
                } catch (Exception ex) {
                    Logger.LogTrace(ex, "Failed to resolve rtsHash hash for {Hash}", Convert.ToHexString(v1));
                    recordResolveBackoff(v1, now);
                }
            }
        }

        protected override async Task<HistoryEntry> PollAsync(CancellationToken CancellationToken)
        {
            using var response = await FetchAllTorrents(CancellationToken);

            var now = DateTime.UtcNow;
            var poll = ++PollCount;
            // Speed window is the last SPEED_HISTORY_MAX polls
            var oldestPoll = poll - SPEED_HISTORY_MAX + 1;

            var unknownV1s = new List<byte[]>();
            var polledLookup = Polled.GetAlternateLookup<ReadOnlySpan<byte>>();
            var lastSnapshot = LastSnapshot;
            // Every torrent is the same as in the last snapshot so far
            var unchanged = lastSnapshot != null;
            PollEntries.Clear();

            var xml = response.Reader();
            XMLUtils.SeekTo(ref xml, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_START);

            while (NextEntry(ref xml, out var entry)) {
                // Response body is one buffer
                Debug.Assert(entry.IsSingleSegment);
                var raw = entry.FirstSpan;

                var hashReader = new SequenceReader<byte>(entry);
                XMLUtils.SeekFixed(ref hashReader, XMLUtils.VALUE_TOKEN);
                XMLUtils.SeekFixed(ref hashReader, XMLUtils.STRING_TOKEN);
                var hashHex = XMLUtils.GetBytesUntilNextTag(ref hashReader).FirstSpan;

                if (!polledLookup.TryGetValue(hashHex, out var polled)) {
                    polled = new PolledTorrent();
                    polledLookup[hashHex] = polled;
                }
                polled.LastPoll = poll;

                var shared = polled.Torrent != null && raw.SequenceEqual(polled.Raw) ? polled.Torrent : null;

                byte[] key;
                Torrent torrent;
                TorrentsTrackersReply.Types.TorrentsTrackers torrentTrackers;
                if (shared != null) {
                    key = KeyOf(polled.NativeHash!, shared.MagnetDummy, unknownV1s);
                    torrent = shared;
                    torrentTrackers = polled.Trackers!;

                    // V2 hash of a hybrid torrent became known since
                    if (!shared.Hash.Span.SequenceEqual(key)) {
                        var keyHash = ByteString.CopyFrom(key);
                        torrentTrackers = new() { InfoHash = keyHash, Trackers = { torrentTrackers.Trackers } };
                        torrent = shared.Clone();
                        torrent.Hash = keyHash;
                    }
                } else {
                    (polled.NativeHash, key, torrent, polled.TrackersRaw, polled.TrackersRunning, torrentTrackers) = ParseEntry(new SequenceReader<byte>(entry), polled, unknownV1s);
                    polled.Raw = raw.ToArray();
                }

                polled.AddSample(poll, now, torrent.Uploaded, torrent.CompletedSize);

                var upSpeed = (ulong)polled.Speed(oldestPoll, Uploaded: true);
                var dlSpeed = torrent.DLSpeed;
                ulong hashingSpeed = 0;
                Duration? eta = null;
                if (torrent.IsCompleted) {
                    dlSpeed = 0;
                } else {
                    var speed = (ulong)polled.Speed(oldestPoll, Uploaded: false);
                    if (torrent.State.HasFlag(TorrentState.Hashing))
                        hashingSpeed = speed;
                    else
                        dlSpeed = speed;

                    if (speed != 0) {
                        var secs = (double)(torrent.WantedSize - torrent.CompletedSize) / speed;
                        eta = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
                    }
                }
                var state = upSpeed > 0 || dlSpeed > 0 || hashingSpeed > 0 ? torrent.State | TorrentState.Active : torrent.State & ~TorrentState.Active;

                if (torrent.UPSpeed != upSpeed || torrent.DLSpeed != dlSpeed || torrent.IOSpeed != hashingSpeed || !Equals(torrent.ETA, eta) || torrent.State != state) {
                    if (ReferenceEquals(torrent, shared))
                        torrent = shared.Clone();

                    torrent.UPSpeed = upSpeed;
                    torrent.DLSpeed = dlSpeed;
                    torrent.IOSpeed = hashingSpeed;
                    torrent.ETA = eta;
                    torrent.State = state;
                }

                polled.Trackers = torrentTrackers;
                polled.Torrent = torrent;

                unchanged = unchanged &&
                    lastSnapshot!.List.TryGetValue(key, out var lastTorrent) && ReferenceEquals(lastTorrent, torrent) &&
                    lastSnapshot.Trackers.TryGetValue(key, out var lastTrackers) && ReferenceEquals(lastTrackers, torrentTrackers);
                PollEntries.Add((key, torrent, torrentTrackers));
            }

            // Torrents gone for the whole speed window
            foreach (var (hashHex, polled) in Polled) {
                if (polled.LastPoll < oldestPoll)
                    Polled.Remove(hashHex);
            }

            if (unchanged && unknownV1s.Count == 0 && PollEntries.Count == lastSnapshot!.List.Count) {
                PollEntries.Clear();
                return LastSnapshot = new HistoryEntry(now, lastSnapshot.List, lastSnapshot.Trackers);
            }

            var torrents = new InfoHashDictionary<Torrent>(PollEntries.Count);
            var trackers = new InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>(PollEntries.Count);
            foreach (var (key, torrent, torrentTrackers) in PollEntries) {
                trackers[key] = torrentTrackers;
                torrents[key] = torrent;
            }
            PollEntries.Clear();

            if (unknownV1s.Count > 0)
                await ResolveUnknownHashes(unknownV1s, torrents, trackers);

            return LastSnapshot = new HistoryEntry(now, torrents, trackers);
        }
    }
}
