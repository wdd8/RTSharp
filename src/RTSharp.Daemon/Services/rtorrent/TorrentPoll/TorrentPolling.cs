using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Utils;

using System.Diagnostics;
using System.Text.RegularExpressions;


namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public partial class TorrentPolling : BackgroundService
    {
        private string InstanceKey { get; }
        private IServiceScopeFactory ScopeFactory { get; }
        private ILogger<TorrentPolling> Logger { get; }
        private ILogger<PollingSubscription> PollingSubscriptionLogger { get; }

        public TorrentPolling(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, ILogger<TorrentPolling> Logger, ILogger<PollingSubscription> PollingSubscriptionLogger)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
            this.Logger = Logger;
            this.PollingSubscriptionLogger = PollingSubscriptionLogger;
        }

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

        private readonly record struct SpeedSample(DateTime Date, InfoHashDictionary<(ulong Uploaded, ulong Downloaded)> Values);

        private readonly List<SpeedSample> GlobalHistory = [];

        private static double ComputeSpeed(List<SpeedSample> History, byte[] Hash, bool Uploaded)
        {
            Span<(DateTime, long)> buf = stackalloc (DateTime, long)[History.Count];
            var c = 0;
            foreach (var h in History) {
                if (h.Values.TryGetValue(Hash, out var t))
                    buf[c++] = (h.Date, (long)(Uploaded ? t.Uploaded : t.Downloaded));
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

        private readonly string AllTorrentsXml = "<?xml version=\"1.0\" ?><methodCall><methodName>d.multicall2</methodName><params><param><value><string></string></value></param><param><value><string>main</string></value></param><param><value><string>"
                + "d.hash=</string></value></param><param><value><string>"
                + "d.name=</string></value></param><param><value><string>"
                + "d.size_bytes=</string></value></param><param><value><string>"
                + "d.completed_bytes=</string></value></param><param><value><string>"
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
                + "d.skip.total=</string></value></param><param><value><string>"
                + "cat=\"$t.multicall=d.hash=,t.url=,cat=^,t.is_enabled=,cat=^,t.is_usable=,cat=^,t.scrape_complete=,cat=^,t.scrape_incomplete=,cat=^,t.scrape_downloaded=,cat=^,t.scrape_time_last=,cat=^,t.normal_interval=,cat=^,t.latest_event=,cat=|\""
                + "</string></value></param><param><value><string>"
                + "d.message=</string></value></param><param><value><string>"
                + "d.is_open=</string></value></param><param><value><string>"
                + "d.is_active=</string></value></param><param><value><string>"
                + "d.is_hash_checking=</string></value></param><param><value><string>"
                + "d.is_private=</string></value></param><param><value><string>"
                + "d.peers_complete=</string></value></param><param><value><string>"
                + "d.peers_accounted=</string></value></param>"
                + "</params></methodCall>";

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

        public async Task<(InfoHashDictionary<Torrent> List, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers)> GetAllTorrents()
        {
            ReadOnlyMemory<byte> xml;

            using (var scope = ScopeFactory.CreateScope()) {
                var comm = scope.ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);

                xml = await comm.Get(AllTorrentsXml);
            }

            XMLUtils.SeekTo(ref xml, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_START);

            var torrents = new InfoHashDictionary<Torrent>();
            var trackers = new InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>();

            Span<Range> trackerRanges = stackalloc Range[64];
            Span<Range> trackerPartRanges = stackalloc Range[9];

            while (!XMLUtils.CheckFor(xml, XMLUtils.DATA_TOKEN_END)) {
                XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                var hashRaw = XMLUtils.GetValue<string>(ref xml);
                var name = XMLUtils.GetValue<string>(ref xml);
                var totalSize = XMLUtils.GetValue<ulong>(ref xml);
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
                var skippedTotal = XMLUtils.GetValue<ulong>(ref xml);

                var trackersRaw = XMLUtils.GetValue<string>(ref xml);

                var statusMessage = XMLUtils.Decode(XMLUtils.GetValue<string>(ref xml));

                var isOpenRaw = XMLUtils.GetValue<bool>(ref xml);
                var isActiveRaw = XMLUtils.GetValue<bool>(ref xml);
                var isHashCheckingRaw = XMLUtils.GetValue<bool>(ref xml);
                var isPrivateRaw = XMLUtils.GetValue<bool>(ref xml);

                var seedersConnected = XMLUtils.GetValue<ulong>(ref xml);
                var peersConnected = XMLUtils.GetValue<ulong>(ref xml);

                XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                var hash = Convert.FromHexString(hashRaw);

                var priority = (TorrentPriority)priorityRaw;

                var curTrackers = new List<TorrentTracker>();
                uint seedersTotal = 0, peersTotal = 0;

                TorrentState torrentState;

                if (isMultiFile) {
                    // If multifile remove d.name from path. Base path shouldn't point to any inner files of torrent
                    var lastSep = directory.LastIndexOf(System.IO.Path.DirectorySeparatorChar);
                    directory = lastSep <= 0 ? "" : directory[..lastSep];
                }

                if (!isOpenRaw)
                    torrentState = totalSize == downloaded ? TorrentState.Complete : TorrentState.Stopped; // TODO: partial downloads
                else {
                    if (isHashCheckingRaw)
                        torrentState = TorrentState.Hashing;
                    else {
                        if (isActiveRaw)
                            torrentState = totalSize == downloaded ? TorrentState.Seeding : TorrentState.Downloading; // TODO: partial downloads
                        else
                            torrentState = TorrentState.Paused;
                    }
                }

                if (statusMessage.Length != 0 && statusMessage != "Tracker: [Tried all trackers.]")
                    torrentState |= TorrentState.Errored;
                
                var trackersRawSpan = trackersRaw.AsSpan();
                var trackerCount = trackersRawSpan.Split(trackerRanges, '|', StringSplitOptions.RemoveEmptyEntries);

                for (var x = 0; x < trackerCount; x++) {
                    var trackerRaw = trackersRawSpan[trackerRanges[x]];
                    var partCount = trackerRaw.Split(trackerPartRanges, '^', StringSplitOptions.RemoveEmptyEntries);
                    Debug.Assert(partCount == 9);

                    var tracker = new TorrentTracker();
                    tracker.ID = tracker.Uri = XMLUtils.Decode(trackerRaw[trackerPartRanges[0]].ToString());
                    TorrentTrackerStatus status;
                    status = trackerRaw[trackerPartRanges[1]] is "1" ? TorrentTrackerStatus.Enabled : TorrentTrackerStatus.Disabled;
                    status |= trackerRaw[trackerPartRanges[2]] is "1" ? TorrentTrackerStatus.Active : TorrentTrackerStatus.NotActive;
                    tracker.Status = status;
                    tracker.Seeders = UInt32.Parse(trackerRaw[trackerPartRanges[3]]);
                    tracker.Peers = UInt32.Parse(trackerRaw[trackerPartRanges[4]]);
                    tracker.Downloaded = UInt32.Parse(trackerRaw[trackerPartRanges[5]]);
                    tracker.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)UInt64.Parse(trackerRaw[trackerPartRanges[6]])));
                    tracker.ScrapeInterval = Duration.FromTimeSpan(TimeSpan.FromSeconds(UInt32.Parse(trackerRaw[trackerPartRanges[7]])));
                    tracker.StatusMessage = TrackerStatusMessage(trackerRaw[trackerPartRanges[8]]);

                    seedersTotal += tracker.Seeders;
                    peersTotal += tracker.Peers;
                    curTrackers.Add(tracker);
                }

                var primaryTracker = PollingSubscription.GetPrimaryTracker(curTrackers);

                if (torrentComment.StartsWith("VRS24mrker"))
                    torrentComment = torrentComment[10..];

                var decodedName = XMLUtils.Decode(name);

                var torrent = new Torrent() {
                    Hash = ByteString.CopyFrom(hash),
                    Name = decodedName,
                    State = torrentState,
                    WantedSize = totalSize, // TODO: rtorrent supports not downloading specific files, this should account for it
                    IsPrivate = isPrivateRaw,
                    Size = totalSize,
                    Downloaded = downloaded,
                    CompletedSize = downloaded, // TODO: probably maybe should fix this in future since its possible to download partials with rtorrent
                    Uploaded = uploaded,
                    DLSpeed = downSpeed,
                    CreatedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)creationDateRaw)),
                    AddedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)addDateRaw)),
                    FinishedOn = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)finishedDateRaw)),
                    // label
                    Comment = XMLUtils.Decode(torrentComment), // rutorrent compat
                    RemotePath = directory,
                    Priority = priority,
                    ChunkSize = (uint)chunkSize,
                    Wasted = skippedTotal,
                    // trackers
                    PrimaryTracker = primaryTracker,
                    StatusMessage = statusMessage,
                    SeedersConnected = (uint)seedersConnected,
                    PeersConnected = (uint)peersConnected,
                    SeedersTotal = seedersTotal,
                    PeersTotal = peersTotal,
                    MagnetDummy = MagnetDummyRegex().IsMatch(decodedName)
                };

                ParseLabels(label, torrent.Labels);
                trackers[hash] = new TorrentsTrackersReply.Types.TorrentsTrackers()
                {
                    InfoHash = ByteString.CopyFrom(hash),
                    Trackers = { curTrackers }
                };

                torrents[hash] = torrent;
            }

            Debug.Assert(xml.Length == 31 + 2 + 9 + 2 + 17 + 2);

            return (torrents, trackers);
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

                var speedValues = new InfoHashDictionary<(ulong Uploaded, ulong Downloaded)>();
                foreach (var (hash, torrent) in allTorrents.List)
                    speedValues[hash] = (torrent.Uploaded, torrent.Downloaded);

                GlobalHistory.Add(new SpeedSample(now, speedValues));
                if (GlobalHistory.Count > SPEED_HISTORY_MAX)
                    GlobalHistory.RemoveAt(0);

                var entry = new HistoryEntry(now, allTorrents.List, allTorrents.Trackers);

                foreach (var (hash, torrent) in allTorrents.List) {
                    var upSpeed = (ulong)ComputeSpeed(GlobalHistory, hash, Uploaded: true);
                    torrent.UPSpeed = upSpeed;

                    if (torrent.CompletedSize >= torrent.WantedSize) {
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
