using Nito.AsyncEx;
using System.Text;
using RTSharp.Shared.Utils;
using RTSharp.Daemon.Protocols.DataProvider;


namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public class PollingSubscription : IDisposable
    {
        public TimeSpan Interval { get; }
        public AsyncAutoResetEvent EvNewDelta { get; }
        public List<HistoryEntry> History { get; } = new();
        public Lock HistoryLock = new();
        
        internal static volatile HistoryEntry? LatestHistory;

        private TorrentPolling Instance;
        private ILogger<PollingSubscription> Logger;

        public PollingSubscription(TimeSpan Interval, TorrentPolling Instance, ILogger<PollingSubscription> Logger)
        {
            this.Interval = Interval;
            EvNewDelta = new AsyncAutoResetEvent(false);

            this.Instance = Instance;
            this.Logger = Logger;
        }

        public async Task<DeltaTorrentsListResponse?> GetChanges(bool AlwaysFullUpdate, CancellationToken CancellationToken)
        {
            InfoHashDictionary<Torrent>? previousList = null;
            InfoHashDictionary<Torrent> currentList;
            InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> currentTrackers;
            InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>? previousTrackers = null;

            await EvNewDelta.WaitAsync(CancellationToken);

            if (CancellationToken.IsCancellationRequested)
                return null;

            lock (HistoryLock) {
                var last = History.Last();
                currentList = last.List;
                currentTrackers = last.Trackers;

                if (!AlwaysFullUpdate) {
                    HistoryEntry? previousEntry = History.Count >= 2 ? History[^2] : null;

                    if (Logger.IsEnabled(LogLevel.Debug)) {
                        var idx = previousEntry != null ? History.IndexOf(previousEntry) : -1;
                        var str = new StringBuilder();
                        for (var x = 0;x < History.Count;x++) {
                            str.AppendLine($"History {x + 1}: {History[x].Date.ToString("O")}{(x == idx ? " <<< previous" : "")}");
                        }

                        Logger.LogDebug(str.ToString());
                    }

                    previousList = previousEntry?.List;
                    previousTrackers = previousEntry?.Trackers;
                }
            }

            var ret = new DeltaTorrentsListResponse();

            foreach (var (hash, torrent) in currentList) {
                var trackers = currentTrackers[hash];

                Torrent? previous = null;
                TorrentsTrackersReply.Types.TorrentsTrackers? curPreviousTrackers = null;

                previousList?.TryGetValue(hash, out previous);
                previousTrackers?.TryGetValue(hash, out curPreviousTrackers);

                void add(bool fullUpdate)
                {
                    if (fullUpdate) {
                        ret.FullUpdate.Add(torrent);
                    } else if (torrent.Downloaded >= torrent.WantedSize) {
                        var toAdd = new CompleteDeltaTorrentResponse() {
                            Hash = torrent.Hash,
                            Uploaded = torrent.Uploaded,
                            UPSpeed = torrent.UPSpeed,
                            SeedersTotal = torrent.SeedersTotal,
                            PeersTotal = torrent.PeersTotal,
                            PeersConnected = torrent.PeersConnected
                        };
                        ret.Complete.Add(toAdd);
                    } else {
                        var toAdd = new IncompleteDeltaTorrentResponse() {
                            Hash = torrent.Hash,
                            Downloaded = torrent.Downloaded,
                            CompletedSize = torrent.CompletedSize,
                            Uploaded = torrent.Uploaded,
                            DLSpeed = torrent.DLSpeed,
                            HashingSpeed = torrent.HashingSpeed,
                            UPSpeed = torrent.UPSpeed,
                            ETA = torrent.ETA,
                            SeedersConnected = torrent.SeedersConnected,
                            SeedersTotal = torrent.SeedersTotal,
                            PeersConnected = torrent.PeersConnected,
                            PeersTotal = torrent.PeersTotal,
                            Wasted = torrent.Wasted
                        };
                        ret.Incomplete.Add(toAdd);
                    }
                }

                if (AlwaysFullUpdate || previous == null) {
                    add(true);
                    continue;
                }

                if (torrent.State != previous.State ||
                    torrent.WantedSize != previous.WantedSize ||
                    torrent.Priority != previous.Priority ||
                    torrent.MagnetDummy != previous.MagnetDummy ||
                    torrent.SequentialDownload != previous.SequentialDownload ||
                    torrent.RemotePath != previous.RemotePath ||
                    torrent.StatusMessage != previous.StatusMessage ||
                    !Equals(torrent.FinishedOn, previous.FinishedOn) ||
                    !torrent.Labels.SequenceEqual(previous.Labels) ||
                    TrackersChanged(trackers, curPreviousTrackers) ||
                    torrent.PrimaryTracker?.Uri != previous.PrimaryTracker?.Uri)
                    add(true);
                else if (torrent.IsCompleted) {
                    // Complete
                    if (torrent.Uploaded != previous.Uploaded ||
                        torrent.UPSpeed != previous.UPSpeed ||
                        torrent.SeedersTotal != previous.SeedersTotal ||
                        torrent.PeersTotal != previous.PeersTotal ||
                        torrent.PeersConnected != previous.PeersConnected) {

                        add(false);
                    }
                } else if (torrent.Uploaded != previous.Uploaded ||
                    torrent.Downloaded != previous.Downloaded ||
                    torrent.CompletedSize != previous.CompletedSize ||
                    torrent.DLSpeed != previous.DLSpeed ||
                    torrent.HashingSpeed != previous.HashingSpeed ||
                    torrent.UPSpeed != previous.UPSpeed ||
                    torrent.SeedersConnected != previous.SeedersConnected ||
                    torrent.SeedersTotal != previous.SeedersTotal ||
                    torrent.PeersConnected != previous.PeersConnected ||
                    torrent.PeersTotal != previous.PeersTotal ||
                    torrent.Wasted != previous.Wasted) {

                    add(false);
                }
            }

            if (previousList != null) {
                foreach (var t in previousList) {
                    if (!currentList.ContainsKey(t.Key))
                        ret.Removed.Add(t.Value.Hash);
                }
            }

            return ret;
        }

        static bool TrackersChanged(TorrentsTrackersReply.Types.TorrentsTrackers Current, TorrentsTrackersReply.Types.TorrentsTrackers? Previous)
        {
            foreach (var x in Current.Trackers) {
                TorrentTracker? prev = null;
                if (Previous != null) {
                    foreach (var y in Previous.Trackers) {
                        if (y.Uri == x.Uri) {
                            prev = y;
                            break;
                        }
                    }
                }
                if (prev == null)
                    return true;
                if (x.Status != prev.Status || x.StatusMessage != prev.StatusMessage || x.LastUpdated != prev.LastUpdated)
                    return true;
            }
            return false;
        }

        internal static TorrentTracker? GetPrimaryTracker(IReadOnlyList<TorrentTracker>? Trackers)
        {
            if (Trackers == null)
                return null;

            TorrentTracker? ret = null;
            for (var x = 0;x < Trackers.Count;x++) {
                var t = Trackers[x];
                if (ret == null || t.Peers > ret.Peers || (t.Peers == ret.Peers && t.Status == TorrentTrackerStatus.Active && ret.Status != TorrentTrackerStatus.Active))
                    ret = t;
            }
            return ret;
        }

        public static (Torrent Torrent, TorrentsTrackersReply.Types.TorrentsTrackers Trackers)? GetLatestHistoryEntry(byte[] Hash)
        {
            var latest = LatestHistory;
            if (latest != null && latest.List.TryGetValue(Hash, out var torrent) && latest.Trackers.TryGetValue(Hash, out var trackers))
                return (torrent, trackers);

            return null;
        }

        public string? GetTorrentName(byte[] Hash) => GetLatestHistoryEntry(Hash)?.Torrent.Name;
        
        public static TorrentTracker[] GetTorrentTrackers(byte[] Hash)
        {
            var entry = GetLatestHistoryEntry(Hash);
            var trackers = entry?.Trackers;
            
            if (trackers == null)
            {
                return [];
            }

            return trackers.Trackers.Select(x => new TorrentTracker
            {
                Uri = x.Uri,
                Status = x.Status,
                Seeders = x.Seeders,
                Peers = x.Peers,
                Downloaded = x.Downloaded,
                LastUpdated = x.LastUpdated,
                ScrapeInterval = x.ScrapeInterval,
                StatusMessage = x.StatusMessage
            }).ToArray();
        }

        public void Dispose()
        {
            lock (Instance.SubscribersLock) {
                Instance.Subscribers.Remove(this);
            }
            Instance.ForceLoop();
        }
    }
}
