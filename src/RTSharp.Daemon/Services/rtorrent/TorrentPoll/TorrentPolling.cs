using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Nito.AsyncEx;

using RTSharp.Daemon.Protocols.DataProvider;
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

        //public TorrentsListResponse LastFetch { get; private set; }

        public TorrentPolling(IServiceScopeFactory ScopeFactory, [ServiceKey] string InstanceKey, ILogger<TorrentPolling> Logger, ILogger<PollingSubscription> PollingSubscriptionLogger)
        {
            this.InstanceKey = InstanceKey;
            this.ScopeFactory = ScopeFactory;
            this.ForceLoop = new AsyncManualResetEvent();
            this.Logger = Logger;
            this.PollingSubscriptionLogger = PollingSubscriptionLogger;
        }

        internal List<PollingSubscription> Subscribers = new List<PollingSubscription>();
        internal object SubscribersLock = new object();
        internal AsyncManualResetEvent ForceLoop;

        public PollingSubscription Subscribe(TimeSpan Interval)
        {
            var ret = new PollingSubscription(Interval, this, PollingSubscriptionLogger);

            lock (SubscribersLock) {
                Subscribers.Add(ret);
            }

            ForceLoop.Set();
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

        [GeneratedRegex(@"(?<!\\),")]
        private static partial Regex NonEscapedCommaRegex();

        enum TorrentTrackerStatusMessage {
            NA = 0,
            Completed = 1,
            Started = 2,
            Stopped = 3,
            Scrape = 4
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

            while (!XMLUtils.CheckFor(xml, XMLUtils.DATA_TOKEN_END)) {
                XMLUtils.SeekFixed(ref xml, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                var hashRaw = XMLUtils.GetValue<string>(ref xml);
                var name = XMLUtils.GetValue<string>(ref xml);
                var totalSize = XMLUtils.GetValue<ulong>(ref xml);
                var downloaded = XMLUtils.GetValue<ulong>(ref xml);
                var uploaded = XMLUtils.GetValue<ulong>(ref xml);

                var downSpeed = XMLUtils.GetValue<ulong>(ref xml);

                var creationDateRaw = XMLUtils.GetValue<ulong>(ref xml);
                ulong addDateRaw;
                if (!UInt64.TryParse(XMLUtils.GetValue<string>(ref xml), out addDateRaw)) {
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

                TorrentState torrentState;

                if (isMultiFile) {
                    // If multifile remove d.name from path. Base path shouldn't point to any inner files of torrent
                    directory = String.Join(System.IO.Path.DirectorySeparatorChar, directory.Split(System.IO.Path.DirectorySeparatorChar)[..^1]);
                }

                if (!isOpenRaw)
                    torrentState = totalSize == downloaded ? TorrentState.Complete : TorrentState.Stopped;
                else {
                    if (isHashCheckingRaw)
                        torrentState = TorrentState.Hashing;
                    else {
                        if (isActiveRaw)
                            torrentState = totalSize == downloaded ? TorrentState.Seeding : TorrentState.Downloading;
                        else
                            torrentState = TorrentState.Paused;
                    }
                }

                if (statusMessage.Length != 0 && statusMessage != "Tracker: [Tried all trackers.]")
                    torrentState |= TorrentState.Errored;
                
                foreach (var trackerRaw in trackersRaw.Split('|', StringSplitOptions.RemoveEmptyEntries)) {
                    var parts = trackerRaw.Split('^', StringSplitOptions.RemoveEmptyEntries);

                    var tracker = new TorrentTracker();
                    tracker.ID = tracker.Uri = XMLUtils.Decode(parts[0]);
                    TorrentTrackerStatus status;
                    status = parts[1] == "1" ? TorrentTrackerStatus.Enabled : TorrentTrackerStatus.Disabled;
                    status |= parts[2] == "1" ? TorrentTrackerStatus.Active : TorrentTrackerStatus.NotActive;
                    tracker.Status = status;
                    tracker.Seeders = UInt32.Parse(parts[3]);
                    tracker.Peers = UInt32.Parse(parts[4]);
                    tracker.Downloaded = UInt32.Parse(parts[5]);
                    tracker.LastUpdated = Timestamp.FromDateTimeOffset(DateTimeOffset.FromUnixTimeSeconds((long)UInt64.Parse(parts[6])));
                    tracker.ScrapeInterval = Duration.FromTimeSpan(TimeSpan.FromSeconds(UInt32.Parse(parts[7])));
                    tracker.StatusMessage = ((TorrentTrackerStatusMessage)Byte.Parse(parts[8])).ToString();
                    
                    if (Byte.Parse(parts[8]) > 4)
                        Logger.LogInformation("StatusMessage " + parts[8]);

                    curTrackers.Add(tracker);
                }

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
                    StatusMessage = statusMessage,
                    SeedersConnected = (uint)seedersConnected,
                    PeersConnected = (uint)peersConnected,
                    SeedersTotal = (uint)curTrackers.Sum(x => x.Seeders),
                    PeersTotal = (uint)curTrackers.Sum(x => x.Peers),
                    MagnetDummy = Regex.IsMatch(decodedName, "magnet:\\?xt=urn:[a-z0-9]+:[a-z0-9]{32,40}&dn=.+&tr=.+")
                };

                torrent.Labels.Add(NonEscapedCommaRegex().Split(label).Where(x => !String.IsNullOrEmpty(x)).Select(XMLUtils.Decode));
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
                    subs = Subscribers.ToArray();
                }

                if (subs.Length > 0) {
                    static TimeSpan GCD(IList<TimeSpan> Input)
                    {
                        TimeSpan GCDInner(TimeSpan a, TimeSpan b)
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

                    delay = GCD(subs.Select(x => x.Interval).ToList());
                }

                Logger.LogTrace("Sleeping for {delay} for torrent poll", delay);
                if (delay == TimeSpan.MaxValue)
                    await Task.WhenAny(Task.Delay(-1), ForceLoop.WaitAsync(stoppingToken));
                else
                    await Task.WhenAny(Task.Delay(delay), ForceLoop.WaitAsync(stoppingToken));
                ForceLoop.Reset();

                (InfoHashDictionary<Torrent> List, InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> Trackers) allTorrents;
                try {
                    allTorrents = await GetAllTorrents();
                } catch (Exception ex) {
                    Logger.LogError(ex, "Torrent poll failed");
                    await Task.WhenAny(Task.Delay(1000), ForceLoop.WaitAsync());
                    continue;
                }

                if (delay == TimeSpan.MaxValue)
                    continue;

                const int HISTORY_MAX = 5;

                foreach (var sub in subs) {
                    if (sub.History.Count != 0 && sub.History.Last().Date + sub.Interval >= DateTime.UtcNow)
                        continue;
                        
                    var history = (DateTime.UtcNow, allTorrents.List, allTorrents.Trackers);
                    sub.History.Add(history);
                    PollingSubscription.LatestHistory = history; 

                    if (sub.History.Count > HISTORY_MAX) {
                        sub.History.RemoveAt(0);
                    }

                    sub.EvNewDelta.Set();
                }
            }
        }
    }
}
