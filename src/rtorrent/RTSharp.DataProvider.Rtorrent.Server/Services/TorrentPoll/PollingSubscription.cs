using Nito.AsyncEx;
using RTSharp.DataProvider.Rtorrent.Protocols;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using RTSharp.Shared.Utils;
using RTSharp.DataProvider.Rtorrent.Server.Utils;

namespace RTSharp.DataProvider.Rtorrent.Server.Services.TorrentPoll
{
    public class PollingSubscription : IDisposable
    {
        public TimeSpan Interval { get; }
        public AsyncAutoResetEvent EvNewDelta { get; }
		public List<(DateTime Date, InfoHashDictionary<Torrent> List)> History { get; } = new();
        public object HistoryLock = new();

		private TorrentPolling Instance;
        private ILogger<PollingSubscription> Logger;

        public PollingSubscription(TimeSpan Interval, TorrentPolling Instance, ILogger<PollingSubscription> Logger)
        {
            this.Interval = Interval;
            this.EvNewDelta = new AsyncAutoResetEvent(false);

            this.Instance = Instance;
            this.Logger = Logger;
        }

        public async Task<DeltaTorrentsListResponse> GetChanges(bool AlwaysFullUpdate, CancellationToken CancellationToken)
        {
			InfoHashDictionary<Torrent>? previousList = null;
			InfoHashDictionary<Torrent> currentList;

            await EvNewDelta.WaitAsync(CancellationToken);

            if (CancellationToken.IsCancellationRequested)
                return null;

            lock (HistoryLock) {
                var last = History.Last();
                currentList = last.List;

                if (!AlwaysFullUpdate) {
	                var previousEntry = History.Count >= 2 ? History[^2] : default;

                    if (Logger.IsEnabled(LogLevel.Debug)) {
                        var idx = History.IndexOf(previousEntry);
                        var str = new StringBuilder();
                        for (var x = 0;x < History.Count;x++) {
                            str.AppendLine($"History {x+1}: {History[x].Date.ToString("O")}{(x == idx ? " <<< previous" : "")}");
						}

                        Logger.LogInformation(str.ToString());
					}

	                previousList = previousEntry.List;
                }
            }

            var ret = new DeltaTorrentsListResponse();

            foreach (var (hash, torrent) in currentList) {
				Torrent? previous = null;

				previousList?.TryGetValue(hash, out previous);

				void add(bool fullUpdate)
				{
					if (fullUpdate) {
						if (torrent.UPSpeed == UInt64.MaxValue)
							torrent.UPSpeed = 0;
						if (torrent.DLSpeed == UInt64.MaxValue)
							torrent.DLSpeed = 0;

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
							Trackers = { torrent.Trackers },
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
							Trackers = { torrent.Trackers },
							StatusMessage = torrent.StatusMessage
						};
						ret.Incomplete.Add(toAdd);
					}
				}

				if (AlwaysFullUpdate || previous == null) {
                    add(true);
                    continue;
				}

				torrent.UPSpeed = (ulong)SpeedMovingAverage.CalculateSpeed(History.Select(x => {
					if (x.List.TryGetValue(hash, out var curTorrent)) {
						return (x.Date, (long)curTorrent.Uploaded);
					}
					return (DateTime.MinValue, 0L);
				}).Where(x => x.Item1 != DateTime.MinValue).ToArray());

				if (torrent.UPSpeed == 0 && previous.UPSpeed != 0) {
					// Force send & reset
					torrent.UPSpeed = UInt64.MaxValue;
				}

				if (torrent.Downloaded == torrent.Size) {
					torrent.ETA = Duration.FromTimeSpan(TimeSpan.Zero);
				} else {
					torrent.DLSpeed = (ulong)SpeedMovingAverage.CalculateSpeed(History.Select(x => {
						if (x.List.TryGetValue(hash, out var curTorrent)) {
							return (x.Date, (long)curTorrent.Downloaded);
						}
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

				if (torrent.State != previous.State) {
					add(true);
				} else if (torrent.Downloaded == torrent.Size) {
					if (torrent.Uploaded != previous.Uploaded ||
                        torrent.UPSpeed != 0 ||
						!torrent.Labels.SequenceEqual(previous.Labels) ||
					    torrent.RemotePath != previous.RemotePath ||
					    torrent.SeedersTotal != previous.SeedersTotal ||
					    torrent.PeersTotal != previous.PeersTotal ||
					    torrent.PeersConnected != previous.PeersConnected ||
					    torrent.Priority != previous.Priority ||
					    torrent.Trackers.Any(x => x.Status != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).Status) ||
					    torrent.Trackers.Any(x => x.StatusMessage != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).StatusMessage) ||
					    torrent.Trackers.Any(x => x.LastUpdated != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).LastUpdated) ||
					    torrent.StatusMessage != previous.StatusMessage) {

                        add(false);
                        continue;
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
					torrent.Trackers.Any(x => x.Status != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).Status) ||
					torrent.Trackers.Any(x => x.StatusMessage != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).StatusMessage) ||
					torrent.Trackers.Any(x => x.LastUpdated != previous.Trackers.FirstOrDefault(y => y.Uri == x.Uri).LastUpdated) ||
					torrent.StatusMessage != previous.StatusMessage) {

                    add(false);
                    continue;
                }
            }

            if (previousList != null) {
	            foreach (var t in previousList) {
	                if (!currentList.ContainsKey(t.Key)) {
	                    ret.Removed.Add(t.Value.Hash);
	                }
	            }
            }

			return ret;
        }

		public string? GetTorrentName(byte[] Hash)
		{
			for (var x = History.Count - 1;x >= 0;x--) {
				if (History[x].List.TryGetValue(Hash, out var torrent)) {
					return torrent.Name;
				}
			}

			return null;
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
