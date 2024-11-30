#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using RTSharp.DataProvider.Rtorrent.Plugin.Server;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;
using static RTSharp.DataProvider.Rtorrent.Common.Extensions;
using File = RTSharp.Shared.Abstractions.File;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
    public class DataProvider : IDataProvider
    {
        private Plugin ThisPlugin { get; }
        public IPluginHost PluginHost { get; }

        public IDataProviderFiles Files { get; }

        public IDataProviderTracker Tracker { get; }

        public IDataProviderStats Stats { get; }

        public CancellationToken Active { get; set; }

        public DataProviderCapabilities Capabilities { get; } = new(
            GetFiles: true,
            GetPeers: true,
            GetTrackers: true,
            GetPieces: true,
            StartTorrent: true,
            PauseTorrent: true,
            StopTorrent: true,
            AddTorrent: true,
            ForceRecheckTorrent: true,
            ReannounceToAllTrackers: true,
            GetDotTorrent: true,
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

        public static readonly Metadata Headers = new Metadata {
            new Metadata.Entry("data-provider", "rtorrent")
        };

        public DataProvider(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
            this.PluginHost = ThisPlugin.Host;

            this.Files = new DataProviderFiles(ThisPlugin);
        }

        private CancellationTokenSource TorrentChangesTokenSource;
        private InfoHashDictionary<Torrent> LatestTorrents = new();
        private ReaderWriterLockSlim LatestTorrentsLock = new();

        public async Task<IEnumerable<Torrent>> GetAllTorrents()
        {
            var client = Clients.Torrents();
            var list = await client.GetTorrentListAsync(new Empty());

            LatestTorrentsLock.EnterReadLock(); {
                LatestTorrents = list.List.Select(TorrentMapper.MapFromProto).ToInfoHashDictionary(x => x.Hash);
            } LatestTorrentsLock.ExitReadLock();

            return list.List.Select(TorrentMapper.MapFromProto);
        }

        public async Task<Torrent> GetTorrent(byte[] Hash)
        {
            Torrent? torrent;
            LatestTorrentsLock.EnterReadLock(); {
                LatestTorrents.TryGetValue(Hash, out torrent);
            } LatestTorrentsLock.ExitReadLock();
            return torrent;
        }

        public async Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken)
        {
            var client = Clients.Torrent();
            var resp = await client.GetTorrentsFilesAsync(new Protocols.Torrents() {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, cancellationToken: cancellationToken);

            var ret = new InfoHashDictionary<(bool MultiFile, IList<File> Files)>();
            
            foreach (var torrent in resp.Reply) {
                var files = new List<File>();
                files.AddRange(torrent.Files.Select(TorrentMapper.MapFromProto));

                // Needs to be set outside mapping because of piece size access
                foreach (var file in files) {
                    file.Downloaded = file.DownloadedPieces * In.Single(x => torrent.InfoHash.SequenceEqual(x.Hash)).PieceSize!.Value;
                }

                ret[torrent.InfoHash.ToByteArray()] = (torrent.MultiFile, files);
            }

            return ret;
        }

        public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken)
        {
            var client = Clients.Torrent();
            var resp = await client.GetTorrentsPeersAsync(new Protocols.Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, cancellationToken: cancellationToken);

            var ret = new InfoHashDictionary<IList<Peer>>();

            foreach (var torrent in resp.Reply) {
                var peer = new List<Peer>();
                peer.AddRange(torrent.Peers.Select(TorrentMapper.MapFromProto));

                ret[torrent.InfoHash.ToByteArray()] = peer;
            }

            return ret;
        }

        public async Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var ret = await client.GetTorrentsTrackers(In.Select(x => x.Hash).ToArray());
            
            return ret;
        }

        public async Task<System.Threading.Channels.ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges(CancellationToken CancellationToken)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var combined = CancellationTokenSource.CreateLinkedTokenSource(Active, CancellationToken);

            var updates = client.GetTorrentChanges(combined.Token);

            return updates;

        }

        private async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ActionForMulti(IList<byte[]> In, Func<Protocols.GRPCTorrentService.GRPCTorrentServiceClient, Protocols.Torrents, AsyncUnaryCall<Protocols.TorrentsReply>> Fx)
        {
            var client = Clients.Torrent();

            IList<(byte[] Hash, IList<Exception> Exceptions)> res;
            try {
                var reply = await Fx(client, new Protocols.Torrents() { Hashes = { In.Select(x => x.ToByteString()) } });
                res = Common.Extensions.ToExceptions(reply);
            } catch (RpcException ex) {
                res = In.Select(x => (x, (IList<Exception>)new[] { ex })).ToList();
            }

            return res;
        }

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StartTorrents(IList<byte[]> In) => ActionForMulti(In, (client, hashes) => client.StartTorrentsAsync(hashes));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> PauseTorrents(IList<byte[]> In) => ActionForMulti(In, (client, hashes) => client.PauseTorrentsAsync(hashes));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> StopTorrents(IList<byte[]> In) => ActionForMulti(In, (client, hashes) => client.StopTorrentsAsync(hashes));

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In)
        {
            var client = Clients.Torrent();

            var res = new List<(byte[] Hashes, IList<Exception>)>();
            try {
                var call = client.AddTorrents();

                for (var x = 0;x < In.Count;x++) {
                    await call.RequestStream.WriteAsync(new Protocols.NewTorrentsData() {
                        TMetadata = new Protocols.NewTorrentsData.Types.Metadata() {
                            Id = x.ToString(),
                            Path = In[x].Options.RemoteTargetPath,
                            Filename = In[x].Filename ?? ""
                        }
                    });
                }
                for (var x = 0;x < In.Count;x++) {
                    foreach (var chunk in In[x].Data.Chunk(4096)) {
                        await call.RequestStream.WriteAsync(new Protocols.NewTorrentsData() {
                            TData = new Protocols.NewTorrentsData.Types.TorrentData() {
                                Id = x.ToString(),
                                Chunk = chunk.ToByteString()
                            }
                        });
                    }
                }

                await call.RequestStream.CompleteAsync();

                var all = await call.ResponseAsync;
                return Common.Extensions.ToExceptions(all);
                
            } catch (RpcException ex) {
                foreach (var torrent in In) {
                    res.Add((Array.Empty<byte>(), new[] { ex }));
                }
            }

            return res;
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ForceRecheck(IList<byte[]> In)
        {
            var res = await ActionForMulti(In, (client, hashes) => client.ForceRecheckTorrentsAsync(hashes));

            var successfulRechecks = res.Where(x => x.Exceptions.Count == 0);

            var sw = Stopwatch.StartNew();

            var stateObserved = new InfoHashDictionary<bool>();

            while (sw.Elapsed < TimeSpan.FromSeconds(5)) {
                LatestTorrentsLock.EnterReadLock(); {
                    foreach (var (hash, _) in successfulRechecks) {
                        if (LatestTorrents.TryGetValue(hash, out var torrent)) {
                            if (torrent.State.HasFlag(TORRENT_STATE.HASHING)) {
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
                            if (!torrent.State.HasFlag(TORRENT_STATE.HASHING)) {
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

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> ReannounceToAllTrackers(IList<byte[]> In) => ActionForMulti(In, (client, hashes) => client.ReannounceToAllTrackersAsync(hashes));

        public Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrents(IList<byte[]> In) => ActionForMulti(In, (client, hashes) => client.RemoveTorrentsAsync(hashes));

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> RemoveTorrentsAndData(IList<byte[]> In)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            var result = await server.RemoveTorrentsAndData(In);
            
            return result;
        }

        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            var result = await server.GetDotTorrents(In);

            return result;
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> MoveDownloadDirectory(IList<(byte[] InfoHash, string TargetDirectory)> In, IList<(string SourceFile, string TargetFile)> Check, IProgress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)> Progress)
        {
            var client = Clients.Torrent();
            var server = PluginHost.AttachedDaemonService;

            var req = new Protocols.MoveDownloadDirectoryArgs();
            var res = new List<(byte[] Hashes, IList<Exception>)>();

            foreach (var (hash, directory) in In) {
                req.Torrents.Add(new Protocols.MoveDownloadDirectoryArgs.Types.TorrentMoveDownloadDirectory() {
                    InfoHash = hash.ToByteString(),
                    TargetDirectory = directory
                });
            }

            if (Check.Select(x => x.TargetFile).Distinct().Count() != Check.Count) {
                throw new ArgumentException("Moving multiple torrents with same destination is currently unsupported");
            }

            req.Move = true;

            var reply = await server.CheckExists(Check.Select(x => x.TargetFile).ToArray());

            var existsCount = reply.Where(x => x.Value).Count();

            if (existsCount == reply.Count) {
                // All exist
                var msgbox = await MessageBoxManager.GetMessageBoxStandard("RT# - rtorrent", "All of the files exist in destination directory, do you want to proceed without moving the files?", ButtonEnum.YesNo, Icon.Info, WindowStartupLocation.CenterOwner).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox == ButtonResult.Yes)
                    req.Move = false;
            } else if (existsCount != 0) {
                var msgbox = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                    ButtonDefinitions = new ButtonDefinition[] {
                        new ButtonDefinition() {
                            Name = "Move (overwrite)",
                            IsDefault = false,
                        },
                        new ButtonDefinition() {
                            Name = "Don't move (may result in recheck failure)",
                            IsDefault = false,
                        },
                        new ButtonDefinition() {
                            Name = "Cancel",
                            IsDefault = true,
                            IsCancel = true
                        }
                    },
                    ContentTitle = "RT# - rtorrent",
                    ContentMessage = "Some of the files exist in destination directory, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox == "Move (overwrite)")
                    req.Move = true;
                else if (msgbox == "Don't move (may result in recheck failure)")
                    req.Move = false;
                else {
                    return res;
                }
            }

            req.Check.AddRange(Check.Select(x => new Protocols.MoveDownloadDirectoryArgs.Types.SourceTargetCheck() {
                SourceFile = x.SourceFile,
                TargetFile = x.TargetFile
            }));

            try {
                var content = client.MoveDownloadDirectory(req);
                await foreach (var progress in content.ResponseStream.ReadAllAsync()) {
                    if (progress.Exception != null) {
                        res.Add((progress.InfoHash.ToByteArray(), new[] { new Exception(progress.Exception.ToFailureString()) }));
                        continue;
                    }
                    if (progress.ETA == null) {
                        Progress.Report((
                            progress.InfoHash.ToByteArray(),
                            progress.File,
                            progress.Moved,
                            null
                        ));
                    } else {
                        Progress.Report((
                            progress.InfoHash.ToByteArray(),
                            progress.File,
                            progress.Moved,
                            progress.CurrentFileIndex + "...\n" +
                            Converters.GetSIDataSize(progress.Speed) + "/s\n" +
                            Converters.ToAgoString(progress.ETA.ToTimeSpan()) + " left"
                        ));
                    }
                }
            } catch (RpcException ex) {
                foreach (var torrent in In) {
                    res.Add((torrent.InfoHash, new[] { ex }));
                }
            }

            return res;
        }

        public async Task<IList<(byte[] Hash, IList<Exception> Exceptions)>> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            var client = Clients.Torrent();

            IList<(byte[] Hash, IList<Exception> Exceptions)> res;
            try {
                var reply = await client.SetLabelsAsync(new Protocols.SetLabelsArgs() {
                    In = {
                        In.Select(x => new Protocols.SetLabelsArgs.Types.TorrentLabelsPair() {
                            InfoHash = x.Hash.ToByteString(),
                            Labels = { x.Labels }
                        })
                    }
                });
                res = reply.ToExceptions();
            } catch (RpcException ex) {
                res = In.Select(x => (x.Hash, (IList<Exception>)new[] { ex })).ToList();
            }

            return res;
        }

        public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = Clients.Torrent();

            var res = new InfoHashDictionary<IList<PieceState>>();

            var reply = await client.GetTorrentsPiecesAsync(new Protocols.Torrents {
                Hashes = { In.Select(x => x.Hash.ToByteString()) }
            }, cancellationToken: cancellationToken);

            foreach (var torrent in reply.Reply) {
                var totalBits = torrent.Bitfield.Length * 8;
                var pieces = new PieceState[totalBits];
                for (var x = 0;x < totalBits;x += 8) {
                    var bitset = torrent.Bitfield[x >> 3];
                    for (var i = 0;i < 8;i++) {
                        if ((bitset & 0x80) == 0)
                            pieces[x+i] = PieceState.NotDownloaded;
                        else
                            pieces[x+i] = PieceState.Downloaded;
                        bitset <<= 1;
                    }
                }

                res[torrent.InfoHash.ToByteArray()] = pieces;
            }

            return res;
        }

        public IPlugin Plugin => ThisPlugin;

        public Notifyable<long> TotalDLSpeed { get; } = new();

        public Notifyable<long> TotalUPSpeed { get; } = new();

        public Notifyable<long> ActiveTorrentCount { get; } = new();
    }
}
