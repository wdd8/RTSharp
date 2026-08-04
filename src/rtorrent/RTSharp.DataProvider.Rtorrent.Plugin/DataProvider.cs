using Avalonia.Controls;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;
using System.Threading.Channels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProvider : IDataProvider
{
    private Plugin ThisPlugin { get; }
    public IDataProviderHost<RtorrentDataProviderInstanceConfig> Host => ThisPlugin.Host;
    IDataProviderHost IDataProvider.Host => Host;
    public Guid GUID => ThisPlugin.GUID;

    public IDataProviderFiles Files { get; }

    public IDataProviderTracker Tracker { get; }

    public IDataProviderPeer Peer { get; }

    public IDataProviderStats Stats { get; }

    public CancellationToken Active { get; set; }

    public DataProviderCapabilities Capabilities { get; } = new(
        GetFiles: true,
        GetFileChanges: true,
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
        FastResumeOnAdd: true,
        MoveDownloadDirectory: true,
        RemoveTorrent: true,
        RemoveTorrentAndData: true,
        AddLabel: true,
        SetLabels: true,
        GetLogs: true,
        SetSequentialDownload: false
    );

    public DataProvider(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;

        this.Files = new DataProviderFiles(ThisPlugin);
        this.Tracker = new DataProviderTracker(ThisPlugin);
        this.Peer = new DataProviderPeer(ThisPlugin);
        this.Stats = new DataProviderStats(ThisPlugin);
    }

    public async Task<Torrent> GetTorrent(byte[] Hash)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        return await client.GetTorrent(Hash);
    }

    public async Task<InfoHashDictionary<TorrentFiles>> GetFiles(IList<Torrent> In, CancellationToken cancellationToken)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var ret = await client.GetTorrentsFiles(In, cancellationToken: cancellationToken);

        return ret;
    }

    public async Task<System.Threading.Channels.ChannelReader<TorrentFilesUpdate>> GetFileChanges(Torrent Torrent, TimeSpan Interval, CancellationToken CancellationToken)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var combined = CancellationTokenSource.CreateLinkedTokenSource(Active, CancellationToken);

        System.Threading.Channels.Channel<TorrentFilesUpdate> channel;
        try {
            channel = client.GetFileChanges(Torrent.Hash, Interval, combined.Token);
        } catch {
            combined.Dispose();
            throw;
        }
        _ = channel.Reader.Completion.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), combined, TaskScheduler.Default);

        return channel;
    }

    public async Task<InfoHashDictionary<IList<Peer>>> GetPeers(IList<Torrent> In, CancellationToken cancellationToken)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var ret = await client.GetTorrentsPeers(In, cancellationToken: cancellationToken);
        
        return ret;
    }

    public async Task<InfoHashDictionary<IList<Tracker>>> GetTrackers(IList<Torrent> In, CancellationToken cancellationToken = default)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var ret = await client.GetTorrentsTrackers(In, cancellationToken);
        
        return ret;
    }

    public async Task<System.Threading.Channels.ChannelReader<ListingChanges<Torrent, Torrent, byte[]>>> GetTorrentChanges(CancellationToken CancellationToken)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var interval = ThisPlugin.Host.Config.ListUpdateInterval;

        var combined = CancellationTokenSource.CreateLinkedTokenSource(Active, CancellationToken);

        System.Threading.Channels.Channel<ListingChanges<Torrent, Torrent, byte[]>> channel;
        try {
            channel = client.GetTorrentChanges(interval, combined.Token);
        } catch {
            combined.Dispose();
            throw;
        }
        _ = channel.Reader.Completion.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), combined, TaskScheduler.Default);

        return channel;
    }

    public async Task<TorrentStatuses> StartTorrents(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        return await client.StartTorrents(In);
    }

    public async Task<TorrentStatuses> PauseTorrents(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        return await client.PauseTorrents(In);
    }

    public async Task<TorrentStatuses> StopTorrents(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        return await client.StopTorrents(In);
    }

    public async Task<TorrentStatuses> AddTorrents(IList<(byte[]? Data, string? MagnetUri, string? Filename, AddTorrentsOptions Options)> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        return await client.AddTorrents(In);
    }

    public async Task<Guid> ForceRecheck(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var id = await client.ForceRecheckTorrents(In);

        return id;
    }

    public async Task<TorrentStatuses> ReannounceToAllTrackers(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var id = await client.ReannounceToAllTrackers(In);

        return id;
    }

    public async Task<TorrentStatuses> RemoveTorrents(IList<byte[]> In)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        var id = await client.RemoveTorrents(In);

        return id;
    }

    public async Task<TorrentStatuses> RemoveTorrentsAndData(IList<Torrent> In)
    {
        var server = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var result = await server.RemoveTorrentsAndData(In);
        
        return result;
    }

    public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<Torrent> In)
    {
        var server = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var result = await server.GetDotTorrents(In);

        return result;
    }

    public async Task<Guid?> MoveDownloadDirectory(InfoHashDictionary<string> In, IList<(string SourceFile, string TargetFile)> Check)
    {
        var server = Host.AttachedDaemonService!;
        var torrentsService = Host.AttachedDaemonService!.GetTorrentsService(this);

        if (Check.Select(x => x.TargetFile).Distinct().Count() != Check.Count) {
            throw new ArgumentException("Moving multiple torrents with same destination is currently unsupported");
        }

        var move = true;
        var deleteSourceFiles = true;

        var reply = await server.CheckExists(Check.Select(x => x.TargetFile).ToArray());

        var existsCount = reply.Count(x => x.Value);

        if (existsCount == reply.Count) {
            // All exist
            var msgbox = await MessageBoxManager.GetMessageBoxStandard(
                title: "RT# - rtorrent",
                text: "All of the file names exist in destination directory, do you want to proceed without moving the files?",
                @enum: ButtonEnum.YesNo,
                icon: Icon.Info,
                windowStartupLocation: WindowStartupLocation.CenterOwner).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);

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
            }).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);

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
                    }).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);
                    
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
            }).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);
            
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
            }).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);
            
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
            }).ShowWindowDialogAsync(ThisPlugin.Host.MainWindow);

            return null;
        }

        var session = await torrentsService.MoveDownloadDirectory(In, move, deleteSourceFiles);

        return session;
    }

    public async Task<TorrentStatuses> SetLabels(IList<(byte[] Hash, string[] Labels)> In)
    {
        var server = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var result = await server.SetLabels(In);
        
        return result;
    }

    public async Task<InfoHashDictionary<IList<PieceState>>> GetPieces(IList<Torrent> In, CancellationToken cancellationToken = default)
    {
        var server = Host.AttachedDaemonService!.GetTorrentsService(this);
        
        var result = await server.GetPieces(In, cancellationToken);
        
        return result;
    }

    public async Task<IEnumerable<Torrent>> GetAllTorrents(CancellationToken cancellationToken = default)
    {
        var client = Host.AttachedDaemonService!.GetTorrentsService(this);

        return await client.GetAllTorrents(cancellationToken);
    }

    public ChannelReader<DataProviderDaemonLogEntry> StreamLogs(int History, CancellationToken CancellationToken) =>
        Host.AttachedDaemonService!.StreamLogs(this, History, CancellationToken);

    public Task SetSequentialDownload(IList<byte[]> In, bool Enabled) => throw new NotSupportedException();
}
