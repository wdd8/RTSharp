using BencodeNET.Parsing;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Newtonsoft.Json.Linq;

using QBittorrent.Client;

using RTSharp.DataProvider.Qbittorrent.Plugin.Mappers;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading.Channels;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
{
    public class DataProvider : IDataProvider
    {
        private Plugin ThisPlugin { get; }
        public IPlugin Plugin => ThisPlugin;
        public IPluginHost PluginHost => GlobalClient.PluginHost;

        public IDataProviderFiles Files { get; }

        public IDataProviderTracker Tracker { get; }

        public IDataProviderStats Stats { get; }

        public DataProviderCapabilities Capabilities { get; } = new(
            GetFiles: true,
            GetPeers: true,
            GetTrackers: true,
            StartTorrent: true,
            PauseTorrent: true,
            StopTorrent: false,
            AddTorrent: true,
            ForceRecheckTorrent: true,
            ReannounceToAllTrackers: true,
            ForceStartTorrentOnAdd: null,
            MoveDownloadDirectory: true,
            RemoveTorrent: true,
            RemoveTorrentAndData: true,
            AddLabel: true,
            AddPeer: false,
            BanPeer: true,
            KickPeer: true,
            SnubPeer: true,
            UnsnubPeer: true,
            SetLabels: true
        );

        
        private CancellationTokenSource TorrentChangesTokenSource;
        private InfoHashDictionary<(Torrent Internal, TorrentInfo External)> LatestTorrents = new();
        private ReaderWriterLockSlim LatestTorrentsLock = new();

        

        public DataProvider(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
            GlobalClient.PluginHost = ThisPlugin.Host;

            this.Files = new DataProviderFiles(ThisPlugin, Init);
            this.Stats = new DataProviderStats(ThisPlugin, Init);

            State.Change(DataProviderState.INACTIVE);
        }

        public string PathCombineFSlash(string a, string b)
        {
            return Path.Combine(a, b).Replace("\\", "/");
        }

        public Notifyable<long> LatencyMs { get; } = new();

        public Notifyable<long> TotalDLSpeed { get; } = new();

        public Notifyable<long> TotalUPSpeed { get; } = new();

        public Notifyable<long> ActiveTorrentCount { get; } = new();

        public Notifyable<DataProviderState> State { get; } = new();

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In)
        {
            var parser = new BencodeNET.Torrents.TorrentParser();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var req in In) {
                tasks.Add(Task.Run(async () => {
                    var torrent = parser.Parse(req.Data); // TODO: memory constraints?
                    var exceptions = new List<Exception>();

                    try {
                        var path = Path.GetTempFileName();
                        await System.IO.File.WriteAllBytesAsync(path, req.Data);

                        await Client.AddTorrentsAsync(new AddTorrentFilesRequest {
                            DownloadFolder = req.Options.RemoteTargetPath,
                            Paused = true,
                            TorrentFiles = { path }
                        });

                        System.IO.File.Delete(path);
                    } catch (Exception ex) {
                        exceptions.Add(ex);
                    }

                    return (torrent.GetInfoHashBytes(), (IList<Exception>)exceptions);
                }));
            }

            return await Task.WhenAll(tasks);
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ForceRecheck(IList<byte[]> In)
        {
            Exception exception = null;

            try {
                await Client.RecheckAsync(In.Select(Convert.ToHexString));
            } catch (Exception ex) {
                exception = ex;
            }

            var res = In.Select(x => (Hash: x, Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }))).ToArray();

            var successfulRechecks = res.Where(x => x.Exceptions.Count == 0);

            var sw = Stopwatch.StartNew();

            var stateObserved = new InfoHashDictionary<bool>();

            while (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                LatestTorrentsLock.EnterReadLock(); {
                    foreach (var (hash, _) in successfulRechecks) {
                        if (LatestTorrents.TryGetValue(hash, out var torrent)) {
                            if (torrent.Internal.State.HasFlag(TORRENT_STATE.HASHING)) {
                                stateObserved[hash] = true;
                                break;
                            }
                        }
                    }
                } LatestTorrentsLock.ExitReadLock();
                await Task.Delay(500);
            }

            while (stateObserved.Count != 0) {
                LatestTorrentsLock.EnterReadLock(); {
                    foreach (var hash in stateObserved.Keys.ToImmutableArray()) {
                        if (LatestTorrents.TryGetValue(hash, out var torrent)) {
                            if (!torrent.Internal.State.HasFlag(TORRENT_STATE.HASHING)) {
                                stateObserved.Remove(hash);
                                break;
                            }
                        }
                    }
                } LatestTorrentsLock.ExitReadLock();
                await Task.Delay(500);
            }

            return res;
        }

        public async Task<IEnumerable<Torrent>> GetAllTorrents()
        {
            await Init();

            var list = await Client.GetTorrentListAsync();

            var torrents = list.Select(x => (External: x, Internal: TorrentMapper.MapFromExternal(x)));

            LatestTorrentsLock.EnterWriteLock(); { 
                foreach (var torrent in torrents) {
                    LatestTorrents[torrent.Internal.Hash] = (torrent.Internal, torrent.External);
                }
            } LatestTorrentsLock.ExitWriteLock();

            return torrents.Select(x => x.Internal);
        }

        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In)
        {
            var buildUri = Client.GetType().GetMethod("BuildUri", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var client = Client.GetType().GetField("_client", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            var ret = new InfoHashDictionary<byte[]>();

            foreach (var hash in In) {
                Uri uri = (Uri)buildUri!.Invoke(Client, new object[] { "api/v2/torrents/export", new (string, string)[] { (key: "hash", value: Convert.ToHexString(hash)) } })!;
                var _client = (HttpClient)client!.GetValue(Client)!;

                var req = await _client.GetAsync(uri);
                var bytes = await req.Content.ReadAsByteArrayAsync();
                ret[hash] = bytes;
            }

            return ret;
        }

        public async Task<InfoHashDictionary<(bool MultiFile, IList<Shared.Abstractions.File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            await Init();

            var files = In.Select(x => (Torrent: x, Files: Client.GetTorrentContentsAsync(Convert.ToHexString(x.Hash), null, cancellationToken))).ToArray();

            await Task.WhenAll(files.Select(x => x.Files));

            InfoHashDictionary<TorrentInfo> external;
            LatestTorrentsLock.EnterReadLock();
            {
                external = In.Select(x => LatestTorrents[x.Hash].External).ToInfoHashDictionary(x => Convert.FromHexString(x.Hash), x => x);
            }
            LatestTorrentsLock.ExitReadLock();

            return files.Select(x => (
                Torrent: x.Torrent,
                Files: (
                    MultiFile: x.Files.Result.First().Name.Contains('/'),
                    Files: (IList<Shared.Abstractions.File>)x.Files.Result.Select(i => {
                        i.Name = i.Name.StartsWith(x.Torrent.Name + '/') ? i.Name[(x.Torrent.Name.Length+1)..] : i.Name;
                        return TorrentMapper.MapFromExternal(i);
                    }).ToList()
                )
            )).ToInfoHashDictionary(x => x.Torrent.Hash, x => x.Files);
        }

        public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            await Init();

            var tasks = In.Select(x => (Torrent: x, Peers: Client.GetPeerPartialDataAsync(Convert.ToHexString(x.Hash), 0, cancellationToken))).ToArray();
            await Task.WhenAll(tasks.Select(x => x.Peers));

            return tasks.Select(x => {
                Debug.Assert(x.Peers.Result.FullUpdate);

                using var scope = PluginHost.CreateScope();
                var smas = scope.ServiceProvider.GetRequiredService<ISpeedMovingAverageService>();

                return (
                    Hash: x.Torrent.Hash,
                    Peers: (IList<Peer>)x.Peers.Result.PeersChanged.Select(i => {
                        ulong peerDLSpeed = 0;

                        if (i.Value.Progress != 1) {
                            var sma = smas.Get(Plugin, $"PeerDLSpeed_{i.Key}");

                            if (i.Value.Progress != null)
                                sma.ValueIfChanged((long)(x.Torrent.Size * i.Value.Progress.Value));

                            peerDLSpeed = (ulong)sma.CalculateSpeed();
                        }

                        return TorrentMapper.MapFromExternal(i.Key, i.Value, peerDLSpeed);
                    }).ToList()
                );
            }).ToInfoHashDictionary(x => x.Hash, x => x.Peers);
        }

        public async Task<Torrent> GetTorrent(byte[] Hash)
        {
            await Init();

            var torrent = await Client.GetTorrentListAsync(new TorrentListQuery {
                Hashes = new[] { Convert.ToHexString(Hash) }
            });

            if (!torrent.Any())
                throw new ArgumentException("Torrent not found");

            return TorrentMapper.MapFromExternal(torrent.First());
        }

        public async Task<ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges()
        {
            await Init();

            TorrentChangesTokenSource?.Cancel();

            var channel = Channel.CreateUnbounded<ListingChanges<Torrent, byte[]>>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            _ = Task.Run(async () => {
                TorrentChangesTokenSource = new CancellationTokenSource();
                try {
                    Debug.Assert(!TorrentChangesTokenSource.Token.IsCancellationRequested);

                    int rid = 1;

                    var pollInternal = PluginHost.PluginConfig.GetValue<TimeSpan>("Server:PollInterval");

                    while (!TorrentChangesTokenSource.Token.IsCancellationRequested) {
                        State.Change(DataProviderState.ACTIVE);

                        var partialData = await Client.GetPartialDataAsync(rid, TorrentChangesTokenSource.Token);

                        rid = partialData.ResponseId;

                        var changes = new List<Torrent>();
                        var removed = new List<byte[]>();
                        var ret = new ListingChanges<Torrent, byte[]>(changes, removed);

                        if (partialData.FullUpdate && partialData.TorrentsChanged != null) {
                            foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                                var hash = Convert.FromHexString(strHash);
                                var internalTorrent = new Torrent(hash);
                                var externalTorrent = new TorrentInfo();
                                TorrentMapper.ApplyFromExternal(internalTorrent, changedTorrent);
                                TorrentMapper.ApplyFromExternal(externalTorrent, changedTorrent);
                                externalTorrent.Hash = strHash;

                                changes.Add(internalTorrent);
                                LatestTorrentsLock.EnterWriteLock();
                                {
                                    LatestTorrents[hash] = (internalTorrent, externalTorrent);
                                }
                                LatestTorrentsLock.ExitWriteLock();
                            }
                        } else if (partialData.TorrentsChanged != null) {
                            foreach (var (strHash, changedTorrent) in partialData.TorrentsChanged) {
                                var hash = Convert.FromHexString(strHash);
                                Torrent internalTorrent;
                                TorrentInfo externalTorrent;

                                try {
                                    LatestTorrentsLock.EnterWriteLock();

                                    if (!LatestTorrents.TryGetValue(hash, out var torrent)) {
                                        internalTorrent = new Torrent(hash);
                                        externalTorrent = new TorrentInfo() {
                                            Hash = strHash
                                        };
                                    } else {
                                        internalTorrent = torrent.Internal;
                                        externalTorrent = torrent.External;
                                    }

                                    TorrentMapper.ApplyFromExternal(internalTorrent, changedTorrent);
                                    TorrentMapper.ApplyFromExternal(externalTorrent, changedTorrent);

                                    changes.Add(internalTorrent);

                                    LatestTorrents[hash] = (internalTorrent, externalTorrent);
                                } finally {
                                    LatestTorrentsLock.ExitWriteLock();
                                }
                            }
                        }

                        if (partialData.TorrentsRemoved != null) {
                            foreach (var hash in partialData.TorrentsRemoved) {
                                removed.Add(Convert.FromHexString(hash));
                            }
                        }

                        await channel.Writer.WriteAsync(ret);

                        LatestTorrentsLock.EnterReadLock();
                        {
                            long dlSpeed = 0, upSpeed = 0, activeTorrents = 0;
                            foreach (var torrent in LatestTorrents) {
                                dlSpeed += (long)torrent.Value.Internal.DLSpeed;
                                upSpeed += (long)torrent.Value.Internal.UPSpeed;
                                activeTorrents += torrent.Value.Internal.State.HasFlag(TORRENT_STATE.ACTIVE) ? 1 : 0;
                            }
                            TotalDLSpeed.Change(dlSpeed);
                            TotalUPSpeed.Change(upSpeed);
                            ActiveTorrentCount.Change(activeTorrents);
                        }
                        LatestTorrentsLock.ExitReadLock();

                        await Task.Delay(pollInternal);
                    }
                } catch (TaskCanceledException) { } catch (Exception ex) {
                    PluginHost.Logger.Error(ex, $"Exception thrown in {nameof(GetTorrentChanges)}");
                    await Task.Delay(500); // hack for offline servers
                    throw;
                } finally {
                    channel.Writer.Complete();
                    State.Change(DataProviderState.INACTIVE);
                }
            });

            return channel.Reader;
        }

        public async Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            await Init();

            var trackers = In.Select(x => (Torrent: x, Trackers: Client.GetTorrentTrackersAsync(Convert.ToHexString(x.Hash), cancellationToken))).ToArray();

            await Task.WhenAll(trackers.Select(x => x.Trackers));

            return trackers.ToInfoHashDictionary(x => x.Torrent.Hash, x => (IList<Tracker>)x.Trackers.Result.Where(x => x.Url.IsAbsoluteUri).Select(TorrentMapper.MapFromExternal).ToList());
        }


        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> MoveDownloadDirectory(IList<(byte[] InfoHash, string TargetDirectory)> In, IList<(string SourceFile, string TargetFile)> Check, IProgress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)> Progress)
        {
            await Init();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var req in In) {
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Client.SetLocationAsync(Convert.ToHexString(req.InfoHash), req.TargetDirectory);
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return (req.InfoHash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }));
                }));
            }

            return await Task.WhenAll(tasks);
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PerformVoidAction(IList<byte[]> In, Func<string, Task> Fx)
        {
            await Init();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var hash in In) {
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Fx(Convert.ToHexString(hash));
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return (hash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }));
                }));
            }

            return await Task.WhenAll(tasks);
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PerformVoidActionMulti(IList<byte[]> In, Func<IList<string>, Task> Fx)
        {
            await Init();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            Exception exception = null;

            try {
                await Fx(In.Select(Convert.ToHexString).ToArray());
            } catch (Exception ex) {
                exception = ex;
            }

            return In.Select(x => (x, exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }))).ToArray();
        }

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PauseTorrents(IList<byte[]> In) => PerformVoidAction(In, x => Client.PauseAsync(x));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ReannounceToAllTrackers(IList<byte[]> In) => PerformVoidAction(In, x => Client.ReannounceAsync(x));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrents(IList<byte[]> In) => PerformVoidActionMulti(In, x => Client.DeleteAsync(x));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrentsAndData(IList<byte[]> In) => PerformVoidActionMulti(In, x => Client.DeleteAsync(x, deleteDownloadedData: true));

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            await Init();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var (hash, labels) in In) {
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Client.ClearTorrentTagsAsync(Convert.ToHexString(hash));
                        await Client.AddTorrentTagsAsync(Convert.ToHexString(hash), labels);
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return (hash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }));
                }));
            }

            return await Task.WhenAll(tasks);
        }

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StartTorrents(IList<byte[]> In) => PerformVoidAction(In, x => Client.ResumeAsync(x));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StopTorrents(IList<byte[]> In) => throw new NotSupportedException();
    }
}
