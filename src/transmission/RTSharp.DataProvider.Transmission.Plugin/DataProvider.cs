using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System.Threading.Channels;

using RTSharp.Shared.Abstractions.Daemon;
using Avalonia.Controls;
using MsBox.Avalonia.Models;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

namespace RTSharp.DataProvider.Transmission.Plugin
{
    public class DataProvider : IDataProvider
    {
        private Plugin ThisPlugin { get; }
        public IPlugin Plugin => ThisPlugin;
        public IPluginHost PluginHost => ThisPlugin.Host;

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
            PauseTorrent: false,
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

        public DataProvider(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;

            this.Files = new DataProviderFiles(ThisPlugin);
            this.Stats = new DataProviderStats(ThisPlugin);
        }

        public Notifyable<long> TotalDLSpeed { get; } = new();

        public Notifyable<long> TotalUPSpeed { get; } = new();

        public Notifyable<long> ActiveTorrentCount { get; } = new();

        public async Task<TorrentStatuses> AddTorrents(IList<(byte[] Data, string? Filename, AddTorrentsOptions Options)> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.AddTorrents(In);
        }

        public async Task<Guid> ForceRecheck(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.ForceRecheckTorrents(In);
        }

        public async Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetAllTorrents(cancellationToken);
        }

        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Torrent> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetDotTorrents(In);
        }

        public async Task<InfoHashDictionary<(bool MultiFile, IList<Shared.Abstractions.File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetTorrentsFiles(In, cancellationToken);
        }

        public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetTorrentsPeers(In, cancellationToken);
        }

        public async Task<Torrent> GetTorrent(byte[] Hash)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetTorrent(Hash);
        }

        public async Task<ChannelReader<ListingChanges<Torrent, byte[]>>> GetTorrentChanges(CancellationToken CancellationToken)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            await GetAllTorrents();

            var combined = CancellationTokenSource.CreateLinkedTokenSource(Active, CancellationToken);

            var updates = client.GetTorrentChanges(combined.Token);

            var channel = System.Threading.Channels.Channel.CreateUnbounded<ListingChanges<Shared.Abstractions.Torrent, byte[]>>(new System.Threading.Channels.UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });
            _ = Task.Run(async () => {
                await foreach (var update in updates.Reader.ReadAllAsync(combined.Token)) {

                    TotalDLSpeed.Change(update.Changes.Sum(x => (long)x.DLSpeed));
                    TotalUPSpeed.Change(update.Changes.Sum(x => (long)x.UPSpeed));
                    ActiveTorrentCount.Change(update.Changes.Where(x => x.State.HasFlag(TORRENT_STATE.ACTIVE)).Count());

                    channel.Writer.TryWrite(update);
                }
            }, combined.Token);
            return channel;
        }

        public async Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetTorrentsTrackers(In, cancellationToken);
        }


        public async Task<Guid?> MoveDownloadDirectory(InfoHashDictionary<string> In, IList<(string SourceFile, string TargetFile)> Check)
        {
            var server = PluginHost.AttachedDaemonService!;
            var torrentsClient = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            if (Check.Select(x => x.TargetFile).Distinct().Count() != Check.Count) {
                throw new ArgumentException("Moving multiple torrents with same destination is currently unsupported");
            }

            var move = true;

            var reply = await server.CheckExists(Check.Select(x => x.TargetFile).ToArray());

            var existsCount = reply.Count(x => x.Value);

            if (existsCount == reply.Count) {
                // All exist
                var msgbox = await MessageBoxManager.GetMessageBoxStandard("RT# - transmission", "All of the file names exist in destination directory, do you want to proceed without moving the files?", ButtonEnum.YesNo, Icon.Info, WindowStartupLocation.CenterOwner).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox == ButtonResult.Yes)
                    move = false;
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
                            Name = "Abort",
                            IsDefault = true,
                            IsCancel = true
                        }
                    },
                    ContentTitle = "RT# - transmission",
                    ContentMessage = "Some of the files exist in destination directory, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox == "Move (overwrite)") {
                    var allowedToDeleteTarget = await server.AllowedToDeleteFiles(Check.Select(x => x.TargetFile));

                    if (allowedToDeleteTarget.Any(x => !x.Value)) {
                        var onlySome = allowedToDeleteTarget.Any(x => x.Value);

                        msgbox = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                            ButtonDefinitions = new ButtonDefinition[] {
                                new ButtonDefinition() {
                                    Name = "Try to overwrite anyway",
                                    IsDefault = false,
                                },
                                new ButtonDefinition() {
                                    Name = "Don't move (may result in recheck failure)",
                                    IsDefault = false,
                                },
                                new ButtonDefinition() {
                                    Name = "Abort",
                                    IsDefault = true,
                                    IsCancel = true
                                }
                            },
                            ContentTitle = "RT# - transmission",
                            ContentMessage = $"There are insufficient permissions to overwrite {(onlySome ? "some of " : "")}target files, how would you like to proceed?",
                            Icon = Icon.Warning,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                        if (msgbox == "Try to overwrite anyway") {
                        } else if (msgbox == "Don't move (may result in recheck failure)")
                            move = false;
                        else
                            return null;
                    }

                    move = true;
                } else if (msgbox == "Don't move (may result in recheck failure)")
                    move = false;
                else
                    return null;
            }

            var allowedToRead = await server.AllowedToReadFiles(Check.Select(x => x.SourceFile));

            if (allowedToRead.Any(x => !x.Value)) {
                var msgbox = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                    ButtonDefinitions = new ButtonDefinition[] {
                        new ButtonDefinition() {
                            Name = "Proceed anyway",
                            IsDefault = false,
                        },
                        new ButtonDefinition() {
                            Name = "Abort",
                            IsDefault = true,
                            IsCancel = true
                        }
                    },
                    ContentTitle = "RT# - transmission",
                    ContentMessage = $"There are insufficient permissions to read one or more source files, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox == "Proceed anyway") {
                } else
                    return null;
            }

            // No pre-check for transmission
            /*var err = await torrentsClient.MoveDownloadDirectoryPreCheck(In, Check, move);

            if (err != null) {
                await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                    ContentTitle = "RT# - transmission",
                    ContentMessage = err,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                return null;
            }*/

            var session = await torrentsClient.MoveDownloadDirectory(In, move, true);

            return session;
        }

        public async Task<TorrentStatuses> StopTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.StopTorrents(In);
        }

        public async Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.ReannounceToAllTrackers(In);
        }

        public async Task<TorrentStatuses> RemoveTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.RemoveTorrents(In);
        }

        public async Task<TorrentStatuses> RemoveTorrentsAndData(IList<Torrent> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.RemoveTorrentsAndData(In);
        }

        public async Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var ret = await client.SetLabels(In);
            await client.QueueTorrentUpdate([.. In.Select(x => x.Hash)]);

            return ret;
        }

        public async Task<TorrentStatuses> StartTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.StartTorrents(In);
        }

        public Task<TorrentStatuses> PauseTorrents(IList<byte[]> In) => throw new NotImplementedException();

        public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetPieces(In, cancellationToken);
        }
    }
}