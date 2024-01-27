using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Threading.Channels;

using Transmission.Net.Api.Entity;
using Transmission.Net.Api;
using RTSharp.DataProvider.Transmission.Plugin.Mappers;
using System.Diagnostics;
using System.Collections.Immutable;
using Microsoft.Extensions.Configuration;

namespace RTSharp.DataProvider.Transmission.Plugin
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
            PauseTorrent: false,
            StopTorrent: true,
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
        private InfoHashDictionary<(Torrent Internal, TorrentView External)> LatestTorrents = new();
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

        public string? FileTransferUrl => throw new NotImplementedException();

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In)
        {
            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var req in In) {
                tasks.Add(Task.Run(async () => {
                    var torrent = Convert.ToBase64String(req.Data); // TODO: memory constraints?
                    var exceptions = new List<Exception>();
                    NewTorrentInfo info = null;

                    try {
                        info = await Client.TorrentAddAsync(new NewTorrent {
                            DownloadDirectory = req.Options.RemoteTargetPath,
                            Paused = true,
                            Metainfo = torrent
                        });
                    } catch (Exception ex) {
                        exceptions.Add(ex);
                    }

                    return (info == null ? null : Convert.FromHexString(info.HashString), (IList<Exception>)exceptions);
                }));
            }

            return await Task.WhenAll(tasks);
            throw null;
        }

        int? Translate(byte[] In)
        {
            if (LatestTorrents.TryGetValue(In, out var outp)) {
                return outp.External.Id;
            }

            return null;
        }

        int[] Translate(IList<Torrent> In)
        {
            var ret = new int[In.Count];
            for (var x = 0;x < ret.Length;x++) {
                var translated = Translate(In[x].Hash) ?? throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x].Hash)}'");
                ret[x] = translated;
            }

            return ret;
        }

        object? TranslateObj(byte[] In)
        {
            if (LatestTorrents.TryGetValue(In, out var outp)) {
                return outp.External.Id;
            }

            return null;
        }

        object[] TranslateObj(IList<byte[]> In)
        {
            var ret = new object[In.Count];

            for (var x = 0;x < In.Count;x++) {
                var translated = Translate(In[x]);
                if (translated == null) {
                    throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x])}'");
                }
                ret[x] = translated.Value;
            }

            return ret;
        }

        int[] Translate(IList<byte[]> In)
        {
            var ret = new int[In.Count];

            for (var x = 0;x < In.Count;x++) {
                var translated = Translate(In[x]);
                if (translated == null) {
                    throw new NullReferenceException($"Cannot translate '{Convert.ToHexString(In[x])}'");
                }
                ret[x] = translated.Value;
            }

            return ret;
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ForceRecheck(IList<byte[]> In)
        {
            Exception? exception = null;

            try {
                await Client.TorrentVerifyAsync(TranslateObj(In));
            } catch (Exception ex) {
                exception = ex;
            }

            var res = In.Select(x => (Hash: x, Exceptions: exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }))).ToArray();

            var successfulRechecks = res.Where(x => x.Exceptions.Count == 0);

            var sw = Stopwatch.StartNew();

            var stateObserved = new InfoHashDictionary<bool>();

            while (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                LatestTorrentsLock.EnterReadLock();
                {
                    foreach (var (hash, _) in successfulRechecks) {
                        if (LatestTorrents.TryGetValue(hash, out var torrent)) {
                            if (torrent.Internal.State.HasFlag(TORRENT_STATE.HASHING)) {
                                stateObserved[hash] = true;
                                break;
                            }
                        }
                    }
                }
                LatestTorrentsLock.ExitReadLock();
                await Task.Delay(500);
            }

            while (stateObserved.Count != 0) {
                LatestTorrentsLock.EnterReadLock();
                {
                    foreach (var hash in stateObserved.Keys.ToImmutableArray()) {
                        if (LatestTorrents.TryGetValue(hash, out var torrent)) {
                            if (!torrent.Internal.State.HasFlag(TORRENT_STATE.HASHING)) {
                                stateObserved.Remove(hash);
                                break;
                            }
                        }
                    }
                }
                LatestTorrentsLock.ExitReadLock();
                await Task.Delay(500);
            }

            return res;
        }

        public async Task<IEnumerable<Torrent>> GetAllTorrents()
        {
            Init();

            var list = await Client.TorrentGetAsync(null, TorrentFields.ALL_FIELDS);

            var torrents = list!.Torrents.Select(x => (External: x, Internal: TorrentMapper.MapFromExternal(x)));

            LatestTorrentsLock.EnterWriteLock();
            {
                foreach (var torrent in torrents) {
                    LatestTorrents[torrent.Internal.Hash] = (torrent.Internal, torrent.External);
                }
            }
            LatestTorrentsLock.ExitWriteLock();

            return torrents.Select(x => x.Internal);
        }

        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In)
        {
            Init();

            /*Client.
            Client.TorrentGetAsync().Result.Torrents[0].TorrentFile

            return ret;*/
            throw null;
        }

        public async Task<InfoHashDictionary<(bool MultiFile, IList<Shared.Abstractions.File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            Init();

            var files = await Client.TorrentGetAsync(Translate(In.Select(x => x.Hash).ToArray()));

            return files!.Torrents.Select(x => (
                Hash: x.HashString,
                Files: (
                    MultiFile: x.Files![0].Name!.Contains('/'),
                    Files: (IList<Shared.Abstractions.File>)x.Files.Select(i => TorrentMapper.MapFromExternal(i, (int)x.PieceSize!.Value)).ToList()
                )
            )).ToInfoHashDictionary(x => Convert.FromHexString(x.Hash!), x => x.Files);
        }

        public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            Init();

            var peers = await Client.TorrentGetAsync(Translate(In.Select(x => x.Hash).ToArray()))!;

            return peers!.Torrents.Select(x => (
                Hash: Convert.FromHexString(x.HashString!),
                Peers: (IList<Peer>)x.Peers!.Select(x => TorrentMapper.MapFromExternal(x)).ToList()
            )).ToInfoHashDictionary(x => x.Hash, x => x.Peers);
        }

        public async Task<Torrent> GetTorrent(byte[] Hash)
        {
            Init();

            var torrent = await Client.TorrentGetAsync(Translate(new[] { Hash }));

            if (!torrent!.Torrents.Any())
                throw new ArgumentException("Torrent not found");

            return TorrentMapper.MapFromExternal(torrent.Torrents.First());
        }

        public async Task<ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges()
        {
            Init();

            TorrentChangesTokenSource?.Cancel();

            var channel = Channel.CreateUnbounded<ListingChanges<Torrent, byte[]>>(new UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            _ = Task.Run(async () => {
                TorrentChangesTokenSource = new CancellationTokenSource();
                try {
                    Debug.Assert(!TorrentChangesTokenSource.Token.IsCancellationRequested);

                    var pollInternal = PluginHost.PluginConfig.GetValue<TimeSpan>("Server:PollInterval");

                    var all = await Client.TorrentGetAsync(null, TorrentFields.ALL_FIELDS);
                    var changes = new List<Torrent>();
                    var removed = new List<byte[]>();
                    var ret = new ListingChanges<Torrent, byte[]>(changes, removed);

                    foreach (var externalTorrent in all!.Torrents) {
                        var hash = Convert.FromHexString(externalTorrent.HashString!);
                        var internalTorrent = TorrentMapper.MapFromExternal(externalTorrent);

                        changes.Add(internalTorrent);
                        LatestTorrentsLock.EnterWriteLock();
                        {
                            LatestTorrents[hash] = (internalTorrent, externalTorrent);
                        }
                        LatestTorrentsLock.ExitWriteLock();
                    }

                    await channel.Writer.WriteAsync(ret);

                    while (!TorrentChangesTokenSource.Token.IsCancellationRequested) {
                        State.Change(DataProviderState.ACTIVE);

                        var request = new TransmissionRequest("torrent-get", new Dictionary<string, object> {
                            { "fields", TorrentFields.ALL_FIELDS },
                            { "ids", "recently-active" }
                        });

                        var sendRequestAsync = Client.GetType().GetMethod("SendRequestAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                        var list = (await (Task<TransmissionResponse>)sendRequestAsync!.Invoke(Client, new[] { request })!).Deserialize<TorrentsResult>();

                        changes = new List<Torrent>();
                        removed = new List<byte[]>();
                        ret = new ListingChanges<Torrent, byte[]>(changes, removed);

                        if (list!.Torrents.Any()) {
                            foreach (var externalTorrent in list!.Torrents) {
                                var hash = Convert.FromHexString(externalTorrent.HashString!);
                                var internalTorrent = TorrentMapper.MapFromExternal(externalTorrent);

                                try {
                                    LatestTorrentsLock.EnterWriteLock();

                                    changes.Add(internalTorrent);

                                    LatestTorrents[hash] = (internalTorrent, externalTorrent);
                                } finally {
                                    LatestTorrentsLock.ExitWriteLock();
                                }
                            }
                        }

                        if (list!.Removed?.Any() == true) {
                            foreach (var externalTorrent in list!.Removed) {
                                removed.Add(Convert.FromHexString(externalTorrent.HashString!));
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
            Init();

            var list = await Client.TorrentGetAsync(Translate(In), TorrentFields.ALL_FIELDS);

            return list!.Torrents.ToInfoHashDictionary(x => Convert.FromHexString(x.HashString!), x => (IList<Tracker>)x.TrackerStats!.Select(TorrentMapper.MapFromExternal).ToList());
        }


        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> MoveDownloadDirectory(IList<(byte[] InfoHash, string TargetDirectory)> In, IList<(string SourceFile, string TargetFile)> Check, IProgress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)> Progress)
        {
            /*await Init();

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

            return await Task.WhenAll(tasks);*/
            throw null;
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PerformVoidAction(IList<byte[]> In, Func<int[], Task> Fx)
        {
            try {
                await Fx(Translate(In));
            } catch (Exception ex) {
                return In.Select(x => (x, (IList<Exception>)new[] { ex })).ToArray();
            }

            return In.Select(x => (x, (IList<Exception>)Array.Empty<Exception>())).ToArray();
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PerformVoidActionObj(IList<byte[]> In, Func<object[], Task> Fx)
        {
            try {
                await Fx(TranslateObj(In));
            } catch (Exception ex) {
                return In.Select(x => (x, (IList<Exception>)new[] { ex })).ToArray();
            }

            return In.Select(x => (x, (IList<Exception>)Array.Empty<Exception>())).ToArray();
        }

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StopTorrents(IList<byte[]> In) => PerformVoidActionObj(In, Client.TorrentStopAsync);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ReannounceToAllTrackers(IList<byte[]> In) => PerformVoidAction(In, x => {
            TransmissionRequest request = new TransmissionRequest("torrent-reannounce", new Dictionary<string, object> { { "ids", x } });
            var sendRequestAsync = Client.GetType().GetMethod("SendRequestAsync", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return (Task<TransmissionResponse>)sendRequestAsync!.Invoke(Client, new[] { request })!;
        });

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrents(IList<byte[]> In) => PerformVoidAction(In, x => Client.TorrentRemoveAsync(x, deleteData: false));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrentsAndData(IList<byte[]> In) => PerformVoidAction(In, x => Client.TorrentRemoveAsync(x, deleteData: true));

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            Init();

            var tasks = new List<Task<(byte[] Hash, IList<Exception> Exceptions)>>();
            foreach (var (hash, labels) in In) {
                tasks.Add(Task.Run(async () => {
                    Exception exception = null;

                    try {
                        await Client.TorrentSetAsync(new global::Transmission.Net.Arguments.TorrentSettings {
                            IDs = Translate(hash),
                            Labels = labels
                        });
                    } catch (Exception ex) {
                        exception = ex;
                    }

                    return (hash, exception == null ? (IList<Exception>)Array.Empty<Exception>() : (new[] { exception }));
                }));
            }

            return await Task.WhenAll(tasks);
        }

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StartTorrents(IList<byte[]> In) => PerformVoidActionObj(In, Client.TorrentStartAsync);

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PauseTorrents(IList<byte[]> In) => throw new NotSupportedException();
    }
}