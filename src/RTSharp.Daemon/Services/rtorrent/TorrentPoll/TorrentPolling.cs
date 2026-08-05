using BencodeNET.Objects;
using BencodeNET.Parsing;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Utils;

using System.Diagnostics;


namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public partial class TorrentPolling : BackgroundService
    {
        private string InstanceKey { get; }
        private IServiceScopeFactory ScopeFactory { get; }
        private V1ToV2InfoHashCache Cache { get; }
        private ILogger<TorrentPolling> Logger { get; }
        private ILogger<PollingSubscription> PollingSubscriptionLogger { get; }

        public TorrentPolling(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, V1ToV2InfoHashCache Cache, ILogger<TorrentPolling> Logger, ILogger<PollingSubscription> PollingSubscriptionLogger)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
            this.Cache = Cache;
            this.Logger = Logger;
            this.PollingSubscriptionLogger = PollingSubscriptionLogger;
        }

        private readonly InfoHashDictionary<(int Attempts, DateTime NextTry)> ResolveBackoff = new();

        internal List<PollingSubscription> Subscribers = [];
        internal Lock SubscribersLock = new();

        private TaskCompletionSource ForceLoopSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal void ForceLoop() => ForceLoopSignal.TrySetResult();

        private async Task WaitForNextCycle(TimeSpan Delay, CancellationToken StoppingToken)
        {
            var forceLoopTask = ForceLoopSignal.Task;
            var delayTask = Delay == TimeSpan.MaxValue ? Task.Delay(-1, StoppingToken) : Task.Delay(Delay, StoppingToken);
            await Task.WhenAny(delayTask, forceLoopTask);
            ForceLoopSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private readonly record struct SpeedSample(DateTime Date, InfoHashDictionary<(ulong Uploaded, ulong CompletedSize)> Values);

        private readonly List<SpeedSample> GlobalHistory = [];

        private static double ComputeSpeed(List<SpeedSample> History, byte[] Hash, bool Uploaded)
        {
            Span<(DateTime, long)> buf = stackalloc (DateTime, long)[History.Count];
            var c = 0;
            foreach (var h in History) {
                if (h.Values.TryGetValue(Hash, out var t))
                    buf[c++] = (h.Date, (long)(Uploaded ? t.Uploaded : t.CompletedSize));
            }
            return SpeedMovingAverage.CalculateSpeed(buf[..c]);
        }

        public PollingSubscription Subscribe(TimeSpan Interval)
        {
            var ret = new PollingSubscription(Interval, this, PollingSubscriptionLogger);

            lock (SubscribersLock) {
                Subscribers.Add(ret);
            }

            ForceLoop();
            return ret;
        }

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
                + "cat=\"$t.multicall=d.hash=,t.url=,cat=^,t.is_enabled=,cat=^,t.is_usable=,cat=^,t.scrape_complete=,cat=^,t.scrape_incomplete=,cat=^,t.scrape_downloaded=,cat=^,t.scrape_time_last=,cat=^,t.normal_interval=,cat=^,t.latest_event=,cat=|\""
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

        public async Task<(InfoHashDictionary<Torrent> List, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers)> GetAllTorrents()
        {
            SCGICommunication.ScgiResponse response;

            using (var scope = ScopeFactory.CreateScope()) {
                var comm = scope.ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
                var version = scope.ServiceProvider.GetRequiredKeyedService<RtorrentVersionService>(InstanceKey);

                response = await comm.Get(AllTorrentsXml(await version.IsV16()));
            }

            using (response) {
                var xml = response.Reader();

                XMLUtils.SeekTo(ref xml, XMLUtils.METHOD_RESPONSE);
                XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_START);

                var torrents = new InfoHashDictionary<Torrent>();
                var trackers = new InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>();

                List<byte[]>? unknownV1s = null;

                Span<Range> trackerRanges = stackalloc Range[64];
                Span<Range> trackerPartRanges = stackalloc Range[9];

                while (!XMLUtils.CheckFor(xml, XMLUtils.DATA_TOKEN_END)) {
                    XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                    var hashRaw = XMLUtils.GetValue<string>(ref xml);
                    var name = XMLUtils.GetValue<string>(ref xml);
                    var totalSize = XMLUtils.GetValue<ulong>(ref xml);
                    var completedBytes = XMLUtils.GetValue<ulong>(ref xml);
                    var downloaded = XMLUtils.GetValue<ulong>(ref xml);
                    var uploaded = XMLUtils.GetValue<ulong>(ref xml);

                    var downSpeed = XMLUtils.GetValue<ulong>(ref xml);

                    var creationDateRaw = XMLUtils.GetValue<ulong>(ref xml);
                    if (!UInt64.TryParse(XMLUtils.GetValue<string>(ref xml), out var addDateRaw)) {
                        addDateRaw = 0;
                    }
                    var finishedDateRaw = XMLUtils.GetValue<ulong>(ref xml);

                    var label = XMLUtils.GetValue<string>(ref xml);
                    var torrentComment = XMLUtils.GetValue<string>(ref xml);
                    var isMultiFile = XMLUtils.GetValue<bool>(ref xml);
                    var directory = XMLUtils.Decode(XMLUtils.GetValue<string>(ref xml));
                    var priorityRaw = XMLUtils.GetValue<ulong>(ref xml);
                    var chunkSize = XMLUtils.GetValue<ulong>(ref xml);
                    var isComplete = XMLUtils.GetValue<bool>(ref xml);
                    var wantedChunks = XMLUtils.GetValue<ulong>(ref xml);
                    var skippedTotal = XMLUtils.GetValue<ulong>(ref xml);

                    var trackersRaw = XMLUtils.GetValue<string>(ref xml);

                    var statusMessage = XMLUtils.Decode(XMLUtils.GetValue<string>(ref xml));

                    var isOpenRaw = XMLUtils.GetValue<bool>(ref xml);
                    var isActiveRaw = XMLUtils.GetValue<bool>(ref xml);
                    var isHashCheckingRaw = XMLUtils.GetValue<bool>(ref xml);
                    var isPrivateRaw = XMLUtils.GetValue<bool>(ref xml);

                    var seedersConnected = XMLUtils.GetValue<ulong>(ref xml);
                    var peersConnected = XMLUtils.GetValue<ulong>(ref xml);
                    var isMeta = XMLUtils.GetValue<bool>(ref xml);

                    XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                    var hash = Convert.FromHexString(hashRaw);

                    byte[] key;
                    if (Cache.TryGetRTSHash(hash, out var rtsHash))
                        key = rtsHash;
                    else {
                        key = hash;
                        if (!isMeta) {
                            unknownV1s ??= new List<byte[]>();
                            unknownV1s.Add(hash);
                        }
                    }

                    var curTrackers = new List<TorrentTracker>();

                    var trackersRawSpan = trackersRaw.AsSpan();
                    var trackerCount = trackersRawSpan.Split(trackerRanges, '|', StringSplitOptions.RemoveEmptyEntries);

                    for (var x = 0; x < trackerCount; x++) {
                        var trackerRaw = trackersRawSpan[trackerRanges[x]];
                        var partCount = trackerRaw.Split(trackerPartRanges, '^', StringSplitOptions.RemoveEmptyEntries);
                        Debug.Assert(partCount == 9);

                        curTrackers.Add(Mapper.MapFromRaw(
                            trackerRaw[trackerPartRanges[0]].ToString(),
                            trackerRaw[trackerPartRanges[1]] is "1",
                            trackerRaw[trackerPartRanges[2]] is "1",
                            UInt32.Parse(trackerRaw[trackerPartRanges[3]]),
                            UInt32.Parse(trackerRaw[trackerPartRanges[4]]),
                            UInt32.Parse(trackerRaw[trackerPartRanges[5]]),
                            UInt64.Parse(trackerRaw[trackerPartRanges[6]]),
                            UInt32.Parse(trackerRaw[trackerPartRanges[7]]),
                            trackerRaw[trackerPartRanges[8]].ToString()));
                    }

                    var torrent = Mapper.MapFromRaw(
                        key, name, totalSize, downloaded, completedBytes, uploaded, downSpeed,
                        creationDateRaw, addDateRaw, finishedDateRaw, label, torrentComment,
                        isMultiFile, directory, priorityRaw, chunkSize, isComplete, wantedChunks, skippedTotal,
                        statusMessage, isOpenRaw, isActiveRaw, isHashCheckingRaw, isPrivateRaw,
                        seedersConnected, peersConnected, isMeta, curTrackers);

                    trackers[key] = new TorrentsTrackersReply.Types.TorrentsTrackers()
                    {
                        InfoHash = ByteString.CopyFrom(key),
                        Trackers = { curTrackers }
                    };

                    torrents[key] = torrent;
                }

                Debug.Assert(xml.Remaining == 31 + 2 + 9 + 2 + 17 + 2);

                if (unknownV1s?.Count > 0)
                    await ResolveUnknownHashes(unknownV1s, torrents, trackers);

                return (torrents, trackers);
            }
        }

        private async Task ResolveUnknownHashes(List<byte[]> UnknownV1s, InfoHashDictionary<Torrent> Torrents, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers)
        {
            var now = DateTime.UtcNow;
            var toResolve = UnknownV1s.Where(v1 => !Cache.Contains(v1) && (!ResolveBackoff.TryGetValue(v1, out var b) || now >= b.NextTry)).ToList();

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
                        if (Torrents.Remove(v1, out var t)) {
                            t.Hash = ByteString.CopyFrom(v2);
                            Torrents[v2] = t;
                        }
                        if (Trackers.Remove(v1, out var tr)) {
                            tr.InfoHash = ByteString.CopyFrom(v2);
                            Trackers[v2] = tr;
                        }
                    }
                } catch (Exception ex) {
                    Logger.LogTrace(ex, "Failed to resolve rtsHash hash for {Hash}", Convert.ToHexString(v1));
                    recordResolveBackoff(v1, now);
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested) {
                TimeSpan delay = TimeSpan.MaxValue;

                PollingSubscription[] subs;

                lock (SubscribersLock) {
                    subs = [.. Subscribers];
                }

                if (subs.Length > 0) {
                    static TimeSpan GCD(IList<TimeSpan> Input)
                    {
                        static TimeSpan GCDInner(TimeSpan a, TimeSpan b)
                        {
                            var aTicks = a.Ticks;
                            var bTicks = b.Ticks;

                            while (bTicks != 0) {
                                var remainder = aTicks % bTicks;
                                aTicks = bTicks;
                                bTicks = remainder;
                            }

                            return new TimeSpan(aTicks);
                        }

                        TimeSpan res = Input[0];
                        for (int i = 1;i < Input.Count;i++) {
                            res = GCDInner(res, Input[i]);
                        }

                        return res;
                    }

                    delay = GCD([.. subs.Select(x => x.Interval)]);
                }

                Logger.LogTrace("Sleeping for {delay} for torrent poll", delay);
                await WaitForNextCycle(delay, stoppingToken);

                (InfoHashDictionary<Torrent> List, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers) allTorrents;
                try {
                    allTorrents = await GetAllTorrents();
                } catch (Exception ex) {
                    if (ex is DataProviderFailure dpf)
                        Logger.LogConnectionFailure(dpf);
                    else
                        Logger.LogError(ex, "Torrent poll failed");
                    await WaitForNextCycle(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                if (delay == TimeSpan.MaxValue)
                    continue;

                const int SPEED_HISTORY_MAX = 5;
                const int SUBSCRIBER_HISTORY_MAX = 2;

                var now = DateTime.UtcNow;

                var speedValues = new InfoHashDictionary<(ulong Uploaded, ulong CompletedSize)>();
                foreach (var (hash, torrent) in allTorrents.List)
                    speedValues[hash] = (torrent.Uploaded, torrent.CompletedSize);

                GlobalHistory.Add(new SpeedSample(now, speedValues));
                if (GlobalHistory.Count > SPEED_HISTORY_MAX)
                    GlobalHistory.RemoveAt(0);

                var entry = new HistoryEntry(now, allTorrents.List, allTorrents.Trackers);

                foreach (var (hash, torrent) in allTorrents.List) {
                    var upSpeed = (ulong)ComputeSpeed(GlobalHistory, hash, Uploaded: true);
                    torrent.UPSpeed = upSpeed;

                    if (torrent.IsCompleted) {
                        torrent.DLSpeed = 0;
                        torrent.HashingSpeed = 0;
                        torrent.ETA = null;
                    } else if (torrent.State.HasFlag(TorrentState.Hashing)) {
                        var hashingSpeed = (ulong)ComputeSpeed(GlobalHistory, hash, Uploaded: false);
                        torrent.HashingSpeed = hashingSpeed;
                        if (hashingSpeed == 0) {
                            torrent.ETA = null;
                        } else {
                            var secs = (double)(torrent.WantedSize - torrent.CompletedSize) / hashingSpeed;
                            torrent.ETA = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
                        }
                    } else {
                        torrent.HashingSpeed = 0;
                        var dlSpeed = (ulong)ComputeSpeed(GlobalHistory, hash, Uploaded: false);
                        torrent.DLSpeed = dlSpeed;
                        if (dlSpeed == 0) {
                            torrent.ETA = null;
                        } else {
                            var secs = (double)(torrent.WantedSize - torrent.CompletedSize) / dlSpeed;
                            torrent.ETA = secs >= Duration.MaxSeconds ? null : Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
                        }
                    }

                    if (torrent.UPSpeed > 0 || torrent.DLSpeed > 0 || torrent.HashingSpeed > 0)
                        torrent.State |= TorrentState.Active;
                }

                PollingSubscription.LatestHistory = entry;

                foreach (var sub in subs) {
                    if (sub.History.Count != 0 && sub.History.Last().Date + sub.Interval >= DateTime.UtcNow)
                        continue;

                    sub.History.Add(entry);
                    if (sub.History.Count > SUBSCRIBER_HISTORY_MAX)
                        sub.History.RemoveAt(0);

                    sub.EvNewDelta.Set();
                }
            }
        }
    }
}
