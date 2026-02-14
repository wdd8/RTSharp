#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;
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

        public DataProvider(Plugin ThisPlugin)
        {
            this.ThisPlugin = ThisPlugin;
            this.PluginHost = ThisPlugin.Host;

            this.Files = new DataProviderFiles(ThisPlugin);
            this.Tracker = new DataProviderTracker(ThisPlugin);
            this.Stats = new DataProviderStats(ThisPlugin);
        }

        public async Task<Torrent> GetTorrent(byte[] Hash)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetTorrent(Hash);
        }

        public async Task<InfoHashDictionary<(bool MultiFile, IList<File> Files)>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var ret = await client.GetTorrentsFiles(In, cancellationToken: cancellationToken);

            return ret;
        }

        public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var ret = await client.GetTorrentsPeers(In, cancellationToken: cancellationToken);
            
            return ret;
        }

        public async Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var ret = await client.GetTorrentsTrackers(In, cancellationToken);
            
            return ret;
        }

        public async Task<System.Threading.Channels.ChannelReader<ListingChanges<Torrent, T, byte[]>>> GetTorrentChanges<T>(ConcurrentInfoHashOwnerDictionary<T> Existing, Action<Daemon.Protocols.DataProvider.IncompleteDeltaTorrentResponse, T> Update, Action<Daemon.Protocols.DataProvider.CompleteDeltaTorrentResponse, T> Update2, CancellationToken CancellationToken)
            where T : class
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var combined = CancellationTokenSource.CreateLinkedTokenSource(Active, CancellationToken);

            return client.GetTorrentChanges(Existing, Update, Update2, combined.Token);
        }

        public async Task<TorrentStatuses> StartTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            return await client.StartTorrents(In);
        }

        public async Task<TorrentStatuses> PauseTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            return await client.PauseTorrents(In);
        }

        public async Task<TorrentStatuses> StopTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            return await client.StopTorrents(In);
        }

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

        public async Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var id = await client.ReannounceToAllTrackers(In);

            return id;
        }

        public async Task<TorrentStatuses> RemoveTorrents(IList<byte[]> In)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            var id = await client.RemoveTorrents(In);

            return id;
        }

        public async Task<TorrentStatuses> RemoveTorrentsAndData(IList<Torrent> In)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var result = await server.RemoveTorrentsAndData(In);
            
            return result;
        }

        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Torrent> In)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var result = await server.GetDotTorrents(In);

            return result;
        }

        public async Task<Guid?> MoveDownloadDirectory(InfoHashDictionary<string> In, IList<(string SourceFile, string TargetFile)> Check)
        {
            var server = PluginHost.AttachedDaemonService!;
            var torrentsService = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            if (Check.Select(x => x.TargetFile).Distinct().Count() != Check.Count) {
                throw new ArgumentException("Moving multiple torrents with same destination is currently unsupported");
            }

            var move = true;
            var deleteSourceFiles = true;

            var reply = await server.CheckExists(Check.Select(x => x.TargetFile).ToArray());

            var existsCount = reply.Count(x => x.Value);

            if (existsCount == reply.Count) {
                // All exist
                var msgbox = await MessageBoxManager.GetMessageBoxStandard("RT# - rtorrent", "All of the file names exist in destination directory, do you want to proceed without moving the files?", ButtonEnum.YesNo, Icon.Info, WindowStartupLocation.CenterOwner).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

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
                    ContentTitle = "RT# - rtorrent",
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
                            ContentTitle = "RT# - rtorrent",
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
                    ContentTitle = "RT# - rtorrent",
                    ContentMessage = $"There are insufficient permissions to read one or more source files, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);
                
                if (msgbox == "Proceed anyway") {
                }
                else
                    return null;
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
                            Name = "Leave them as-is",
                            IsDefault = false,
                        },
                        new ButtonDefinition() {
                            Name = "Abort",
                            IsDefault = true,
                            IsCancel = true
                        }
                    },
                    ContentTitle = "RT# - rtorrent",
                    ContentMessage = $"There are insufficient permissions to delete {(onlySome ? "some " : "")}source files after move, how would you like to proceed?",
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);
                
                if (msgbox == "Try to delete anyway")
                    deleteSourceFiles = true;
                else if (msgbox == "Leave them as-is")
                    deleteSourceFiles = false;
                else
                    return null;
            }

            var err = await torrentsService.MoveDownloadDirectoryPreCheck(In, Check, move);

            if (err != null) {
                await MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                    ContentTitle = "RT# - rtorrent",
                    ContentMessage = err,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                }).ShowWindowDialogAsync((Window)PluginHost.MainWindow);

                return null;
            }

            var session = await torrentsService.MoveDownloadDirectory(In, move, deleteSourceFiles);

            return session;
        }

        public async Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var result = await server.SetLabels(In);
            
            return result;
        }

        public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default)
        {
            var server = PluginHost.AttachedDaemonService.GetTorrentsService(this);
            
            var result = await server.GetPieces(In, cancellationToken);
            
            return result;
        }

        public async Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken = default)
        {
            var client = PluginHost.AttachedDaemonService.GetTorrentsService(this);

            return await client.GetAllTorrents(cancellationToken);
        }

        public IPlugin Plugin => ThisPlugin;
    }
}
