using Avalonia.Controls;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;

using System.Threading;
using System.Threading.Channels;

namespace RTSharp.DataProvider.Qbittorrent.Plugin
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
            PauseTorrent: true,
            StopTorrent: false,
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
            this.Stats = null;
        }

        public string PathCombineFSlash(string a, string b)
        {
            return Path.Combine(a, b).Replace("\\", "/");
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

            var id = await client.ForceRecheckTorrents(In);

            return id;
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

            var allowedToDelete = await server.AllowedToDeleteFiles(Check.Select(x => x.SourceFile));

            if (allowedToDelete.Any(x => !x.Value)) {
                var onlySome = allowedToDelete.Any(x => x.Value);
                var msgbox = await MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                    ButtonDefinitions = new ButtonDefinition[] {
                        new ButtonDefinition() {
                            Name = "Try to delete anyway",
                            IsDefault = false,
                        },
                        new ButtonDefinition() {
                            Name = "Abort",
                            IsDefault = true,
                            IsCancel = true
                        }
                    },
                    ContentTitle = "RT# - qbittorrent",
                    ContentMessage = $"There are insufficient permissions to delete {(onlySome ? "some " : "")}source files after move, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                if (msgbox != "Try to delete anyway")
                    return null;
            }

            var session = await torrentsClient.MoveDownloadDirectory(In, true, true); // move/delete args don't do anything

            return session;
        }
        public async Task<TorrentStatuses> PauseTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.PauseTorrents(In);
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

            return await client.SetLabels(In);
        }

        public async Task<TorrentStatuses> StartTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.StartTorrents(In);
        }

        public Task<TorrentStatuses> StopTorrents(IList<byte[]> In) => throw new NotSupportedException();

        public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetPieces(In, cancellationToken);
        }
    }
}
