using Nito.AsyncEx;
using System.Text;
using Google.Protobuf.WellKnownTypes;
using RTSharp.Shared.Utils;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Utils;

using HistoryEntry = (System.DateTime Date, RTSharp.Shared.Utils.InfoHashDictionary<RTSharp.Daemon.Protocols.DataProvider.Torrent> List, RTSharp.Shared.Utils.InfoHashDictionary<RTSharp.Daemon.Protocols.DataProvider.TorrentsTrackersReply.Types.TorrentsTrackers> Trackers);

namespace RTSharp.Daemon.Services.rtorrent.TorrentPoll
{
    public class PollingSubscription : IDisposable
    {
        public TimeSpan Interval { get; }
        public AsyncAutoResetEvent EvNewDelta { get; }
        public List<HistoryEntry> History { get; } = new();
        public Lock HistoryLock = new();
        
        internal static HistoryEntry LatestHistory;

        private TorrentPolling Instance;
        private ILogger<PollingSubscription> Logger;

        public PollingSubscription(TimeSpan Interval, TorrentPolling Instance, ILogger<PollingSubscription> Logger)
        {
            this.Interval = Interval;
            EvNewDelta = new AsyncAutoResetEvent(false);

            this.Instance = Instance;
            this.Logger = Logger;
        }

        public async Task<DeltaTorrentsListResponse>? GetChanges(bool AlwaysFullUpdate, CancellationToken CancellationToken)
        {
            InfoHashDictionary<Torrent>? previousList = null;
            InfoHashDictionary<Torrent> currentList;
            InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers> currentTrackers;
            InfoHashDictionary<TorrentsTrackersReply.Types.TorrentsTrackers>? previousTrackers = null;

            await EvNewDelta.WaitAsync(CancellationToken);

            if (CancellationToken.IsCancellationRequested)
                return null;

            HistoryEntry[] history;

            lock (HistoryLock) {
                var last = History.Last();
                currentList = last.List;
                currentTrackers = last.Trackers;

                if (!AlwaysFullUpdate) {
                    var previousEntry = History.Count >= 2 ? History[^2] : default;

                    if (Logger.IsEnabled(LogLevel.Debug)) {
                        var idx = History.IndexOf(previousEntry);
                        var str = new StringBuilder();
                        for (var x = 0;x < History.Count;x++) {
                            str.AppendLine($"History {x + 1}: {History[x].Date.ToString("O")}{(x == idx ? " <<< previous" : "")}");
                        }

                        Logger.LogInformation(str.ToString());
                    }

                    previousList = previousEntry.List;
                    previousTrackers = previousEntry.Trackers;
                }

                history = History.ToArray();
            }

            var ret = new DeltaTorrentsListResponse();

            foreach (var (hash, torrent) in currentList) {
                var trackers = currentTrackers[hash];
                
                Torrent? previous = null;
                TorrentsTrackersReply.Types.TorrentsTrackers? curPreviousTrackers = null;

                previousList?.TryGetValue(hash, out previous);
                previousTrackers?.TryGetValue(hash, out curPreviousTrackers);

                var primaryTracker = trackers.Trackers.OrderByDescending(x => x.Peers).ThenByDescending(x => x.Status == TorrentTrackerStatus.Active).FirstOrDefault();

                void add(bool fullUpdate)
                {                
                    if (fullUpdate) {
                        if (torrent.UPSpeed == UInt64.MaxValue)
                            torrent.UPSpeed = 0;
                        if (torrent.DLSpeed == UInt64.MaxValue)
                            torrent.DLSpeed = 0;

                        torrent.PrimaryTracker = primaryTracker;

                        ret.FullUpdate.Add(torrent);
                    } else if (torrent.Downloaded == torrent.Size) {
                        var toAdd = new CompleteDeltaTorrentResponse() {
                            Hash = torrent.Hash,
                            State = torrent.State,
                            Uploaded = torrent.Uploaded,
                            UPSpeed = torrent.UPSpeed == UInt64.MaxValue ? 0 : torrent.UPSpeed,
                            Labels = { torrent.Labels },
                            RemotePath = torrent.RemotePath,
                            SeedersTotal = torrent.SeedersTotal,
                            PeersTotal = torrent.PeersTotal,
                            PeersConnected = torrent.PeersConnected,
                            Priority = torrent.Priority,
                            PrimaryTracker = primaryTracker,
                            StatusMessage = torrent.StatusMessage
                        };
                        ret.Complete.Add(toAdd);
                    } else {
                        var toAdd = new IncompleteDeltaTorrentResponse() {
                            Hash = torrent.Hash,
                            State = torrent.State,
                            Downloaded = torrent.Downloaded,
                            Uploaded = torrent.Uploaded,
                            DLSpeed = torrent.DLSpeed == UInt64.MaxValue ? 0 : torrent.DLSpeed,
                            UPSpeed = torrent.UPSpeed == UInt64.MaxValue ? 0 : torrent.UPSpeed,
                            ETA = torrent.ETA,
                            FinishedOn = torrent.FinishedOn,
                            Labels = { torrent.Labels },
                            RemotePath = torrent.RemotePath,
                            SeedersConnected = torrent.SeedersConnected,
                            SeedersTotal = torrent.SeedersTotal,
                            PeersConnected = torrent.PeersConnected,
                            PeersTotal = torrent.PeersTotal,
                            Priority = torrent.Priority,
                            Wasted = torrent.Wasted,
                            PrimaryTracker = primaryTracker,
                            StatusMessage = torrent.StatusMessage
                        };
                        ret.Incomplete.Add(toAdd);
                    }
                }

                if (AlwaysFullUpdate || previous == null) {
                    add(true);
                    continue;
                }

                torrent.UPSpeed = (ulong)SpeedMovingAverage.CalculateSpeed(history.Select(x => {
                    if (x.List.TryGetValue(hash, out var curTorrent))
                        return (x.Date, (long)curTorrent.Uploaded);
                    return (DateTime.MinValue, 0L);
                }).Where(x => x.Item1 != DateTime.MinValue).ToArray());

                if (torrent.UPSpeed == 0 && previous.UPSpeed != 0)                     // Force send & reset
                    torrent.UPSpeed = UInt64.MaxValue;

                if (torrent.Downloaded == torrent.Size)
                    torrent.ETA = Duration.FromTimeSpan(TimeSpan.Zero);
                else {
                    torrent.DLSpeed = (ulong)SpeedMovingAverage.CalculateSpeed(history.Select(x => {
                        if (x.List.TryGetValue(hash, out var curTorrent))
                            return (x.Date, (long)curTorrent.Downloaded);
                        return (DateTime.MinValue, 0L);
                    }).Where(x => x.Item1 != DateTime.MinValue).ToArray());

                    if (torrent.DLSpeed == 0 && previous.DLSpeed != 0) {
                        // Force send & reset
                        torrent.DLSpeed = UInt64.MaxValue;
                        torrent.ETA = null;
                    } else {
                        var secs = (double)(torrent.Size - torrent.Downloaded) / torrent.DLSpeed;
                        if (secs >= Duration.MaxSeconds)
                            torrent.ETA = null;
                        else
                            torrent.ETA = Duration.FromTimeSpan(TimeSpan.FromSeconds(secs));
                    }
                }

                if (torrent.UPSpeed > 0 || torrent.DLSpeed > 0)
                    torrent.State |= TorrentState.Active;

                if (torrent.State != previous.State)
                    add(true);
                else if (torrent.Downloaded == torrent.Size) {
                    if (torrent.Uploaded != previous.Uploaded ||
                        torrent.UPSpeed != 0 ||
                        !torrent.Labels.SequenceEqual(previous.Labels) ||
                        torrent.RemotePath != previous.RemotePath ||
                        torrent.SeedersTotal != previous.SeedersTotal ||
                        torrent.PeersTotal != previous.PeersTotal ||
                        torrent.PeersConnected != previous.PeersConnected ||
                        torrent.Priority != previous.Priority ||
                        trackers.Trackers.Any(x => x.Status != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.Status) ||
                        trackers.Trackers.Any(x => x.StatusMessage != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.StatusMessage) ||
                        trackers.Trackers.Any(x => x.LastUpdated != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.LastUpdated) ||
                        torrent.PrimaryTracker?.Uri != primaryTracker?.Uri ||
                        torrent.StatusMessage != previous.StatusMessage) {

                        add(false);
                    }
                } else if (torrent.Downloaded != previous.Downloaded ||
                    torrent.DLSpeed != 0 ||
                    torrent.Uploaded != previous.Uploaded ||
                    torrent.FinishedOn.CompareTo(torrent.FinishedOn) != 0 ||
                    !torrent.Labels.SequenceEqual(previous.Labels) ||
                    torrent.RemotePath != previous.RemotePath ||
                    torrent.SeedersConnected != previous.SeedersConnected ||
                    torrent.SeedersTotal != previous.SeedersTotal ||
                    torrent.PeersConnected != previous.PeersConnected ||
                    torrent.PeersTotal != previous.PeersTotal ||
                    torrent.Priority != previous.Priority ||
                    torrent.Wasted != previous.Wasted ||
                    trackers.Trackers.Any(x => x.Status != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.Status) ||
                    trackers.Trackers.Any(x => x.StatusMessage != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.StatusMessage) ||
                    trackers.Trackers.Any(x => x.LastUpdated != curPreviousTrackers?.Trackers.FirstOrDefault(y => y.Uri == x.Uri)?.LastUpdated) ||
                    torrent.PrimaryTracker?.Uri != primaryTracker?.Uri ||
                    torrent.StatusMessage != previous.StatusMessage) {

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

        public static (Torrent Torrent, TorrentsTrackersReply.Types.TorrentsTrackers Trackers)? GetLatestHistoryEntry(byte[] Hash)
        {
            if (LatestHistory.List.TryGetValue(Hash, out var torrent) && LatestHistory.Trackers.TryGetValue(Hash, out var trackers))
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
            Instance.ForceLoop.Set();
        }
    }
}
