using Avalonia.Controls;

using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Models;

using RTSharp.Core.TorrentPolling;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;
using RTSharp.ViewModels.TorrentEditor;
using RTSharp.Views;
using RTSharp.Views.TorrentEditor;

using Serilog;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;

namespace RTSharp.ViewModels.TorrentListing;

public partial class TorrentListingViewModel
{
    public Func<Window, ulong, int, Task<bool>> RecheckTorrentsConfirmationDialog { get; set; } = null!; // view set

    public Func<Window, string[], string[], string, Task<bool>> MoveDownloadDirectoryConfirmationDialog { get; set; } = null!; // view set

    public Func<Window, string, Task<string?>> SelectDirectoryDialog { get; set; } = null!; // view set

    public Func<Window, string, IDataProvider, string?, Task<string?>> SelectRemoteDirectoryDialog { get; set; } = null!; // view set

    public Func<Window, ulong, int, bool, Task<bool>> DeleteTorrentsConfirmationDialog { get; set; } = null!; // view set

    public Action ShowAddLabelDialog { get; set; } = null!; // view set

    public Action CloseAddLabelDialog { get; set; } = null!; // view set

    private static async Task ActionForMulti(IReadOnlyList<Torrent> In, string ActionName, Func<RTSharpDataProvider, IList<Torrent>, Task<TorrentStatuses>> Fx)
    {
        In = [.. In]; // cp
        try {
            var actions = In.GroupBy(x => x.DataOwner).Select(x => (
                Core.ActionQueue.GetActionQueueEntry(x.Key.Host),
                ActionQueueAction.New(ActionName, async (action) => {
                    var result = await Fx(x.Key, x.ToList());
                    if (result.Any(x => x.Exceptions.Any())) {
                        throw new Exception("Operation failed:\n" + String.Join('\n', result.Select(x => Convert.ToHexString(x.Hash) + ": " + String.Join(", ", x.Exceptions.Select(x => x.Message)))));
                    }
                })
            ));
            await Task.WhenAll(
                actions.Select(kv => kv.Item1!.Queue.RunAction(kv.Item2))
            );
        } catch { }
    }

    static readonly FrozenDictionary<string, Func<IDataProvider, bool>> StrToCap = new Dictionary<string, Func<IDataProvider, bool>>() {
        { "Start", x => x.Capabilities.StartTorrent },
        { "Pause", x => x.Capabilities.PauseTorrent },
        { "Stop", x => x.Capabilities.StopTorrent },
        { "Force recheck", x => x.Capabilities.ForceRecheckTorrent },
        { "Reannounce to all trackers", x => x.Capabilities.ReannounceToAllTrackers },
        { "Move download directory", x => x.Capabilities.MoveDownloadDirectory },
        { "Remove torrent", x => x.Capabilities.RemoveTorrent },
        { "Remove torrent & data", x => x.Capabilities.RemoveTorrentAndData },
        { "Get .torrent", x => x.Capabilities.GetDotTorrent },
        { "Edit torrent", x => x.Capabilities.GetDotTorrent },
        { "Add label", x => x.Capabilities.AddLabel },
        { "Download sequentially", x => x.Capabilities.SetSequentialDownload },
        { "Duplicate torrent to", x => x.Capabilities.AddTorrent && x.Capabilities.ForceRecheckTorrent && x.Capabilities.StartTorrent }
    }.ToFrozenDictionary();

    bool CanExecuteAction(string Action)
    {
        var dps = SelectedItems.GroupBy(x => x.DataOwner).DistinctBy(x => x.Key.Host.Instance.GUID).Select(x => x.Key);
        bool startTorrentCap = dps.All(x => x.Instance.Capabilities.StartTorrent);
        bool pauseTorrentCap = dps.All(x => x.Instance.Capabilities.PauseTorrent);
        bool stopTorrentCap = dps.All(x => x.Instance.Capabilities.StopTorrent);

        Debug.Assert(StrToCap.ContainsKey(Action));

        if (SelectedItems.Count == 1) {
            var currentlySelectedTorrent = SelectedItems[0];
            return Action switch {
                "Start" => currentlySelectedTorrent.InternalState != TORRENT_STATE.DOWNLOADING &&
                            currentlySelectedTorrent.InternalState != TORRENT_STATE.SEEDING &&
                            currentlySelectedTorrent.InternalState != TORRENT_STATE.HASHING,
                "Pause" => currentlySelectedTorrent.InternalState != TORRENT_STATE.PAUSED &&
                            currentlySelectedTorrent.InternalState != TORRENT_STATE.STOPPED &&
                            currentlySelectedTorrent.InternalState != TORRENT_STATE.COMPLETE,
                "Stop" => currentlySelectedTorrent.InternalState != TORRENT_STATE.STOPPED,
                _ => true
            } && StrToCap[Action](currentlySelectedTorrent.DataOwner.Instance);
        } else
            return dps.All(x => StrToCap[Action](x.Instance));
    }

    public bool CanExecuteStartTorrents() => CanExecuteAction("Start");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteStartTorrents))]
    public async Task StartTorrents(IReadOnlyList<Torrent> In) => await ActionForMulti(In, "Start torrents", (dp, torrents) => dp.Instance.StartTorrents(torrents.Select(x => x.Hash).ToList()));

    public bool CanExecutePauseTorrents() => CanExecuteAction("Pause");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecutePauseTorrents))]
    public async Task PauseTorrents(IReadOnlyList<Torrent> In) => await ActionForMulti(In, "Pause torrents", (dp, torrents) => dp.Instance.PauseTorrents(torrents.Select(x => x.Hash).ToList()));

    public bool CanExecuteStopTorrents() => CanExecuteAction("Stop");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteStopTorrents))]
    public async Task StopTorrents(IReadOnlyList<Torrent> In) => await ActionForMulti(In, "Stop torrents", (dp, torrents) => dp.Instance.StopTorrents(torrents.Select(x => x.Hash).ToList()));

    public bool CanExecuteForceRecheckTorrents() => CanExecuteAction("Force recheck");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteForceRecheckTorrents))]
    public async Task ForceRecheckTorrents(IReadOnlyList<Torrent> In)
    {
        In = [.. In]; // cp
        var result = await RecheckTorrentsConfirmationDialog(App.MainWindow, (ulong)In.Sum(x => (decimal)x.WantedSize), In.Count);

        if (result)
            await ActionForMulti(In, "Force recheck", async (dp, torrents) => {
                var guid = await dp.Instance.ForceRecheck(torrents.Select(x => x.Hash).ToList());
                
                // Wait for completion
                await dp.AttachedDaemonService.GetScriptProgress(guid, null);
                
                // TODO: report proper status
                return [.. torrents.Select(x => (x.Hash, (IList<Exception>)[]))];
            });
    }

    public bool CanExecuteReannounceToAllTrackers() => CanExecuteAction("Reannounce to all trackers");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteReannounceToAllTrackers))]
    public async Task ReannounceToAllTrackers(IReadOnlyList<Torrent> In) => await ActionForMulti(In, "Reannounce to all trackers", (dp, torrents) => dp.Instance.ReannounceToAllTrackers(torrents.Select(x => x.Hash).ToList()));

    public bool CanExecuteSetSequentialDownload() => CanExecuteAction("Download sequentially");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteSetSequentialDownload))]
    public async Task SetSequentialDownload(IReadOnlyList<Torrent> In)
    {
        In = [.. In]; // cp
        if (In.Count == 0)
            return;

        var enable = In.Any(x => !x.IsSequentialDownload);

        try {
            var actions = In.GroupBy(x => x.DataOwner).Select(x => (
                Core.ActionQueue.GetActionQueueEntry(x.Key.Host),
                ActionQueueAction.New("Set sequential download", async (action) => {
                    var torrents = x.ToArray();
                    var prevStates = new bool[torrents.Length];
                    for (var i = 0;i < torrents.Length;i++) {
                        // set in hopes that update will set it to what this is anyway, but this is faster
                        prevStates[i] = torrents[i].IsSequentialDownload;
                        torrents[i].IsSequentialDownload = enable;
                    }

                    try {
                        await x.Key.Instance.SetSequentialDownload([.. x.Select(t => t.Hash)], enable);
                    } catch (Exception ex) {
                        Log.Logger.Error(ex, "Failed to set sequential download value");

                        for (var i = 0;i < torrents.Length;i++) {
                            torrents[i].IsSequentialDownload = prevStates[i];
                        }
                    }
                })
            ));
            await Task.WhenAll(
                actions.Select(kv => kv.Item1!.Queue.RunAction(kv.Item2))
            );
        } catch { }
    }

    record MoveData(byte[] Hash, string BasePath, string ContainerFolder, IEnumerable<string> Files);

    public bool CanExecuteMoveDownloadDirectory() => CanExecuteAction("Move download directory");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteMoveDownloadDirectory))]
    public async Task MoveDownloadDirectory(IReadOnlyList<Torrent> In)
    {
        In = [.. In]; // cp
        var allTorrents = In.ToInfoHashDictionary(x => x.Hash);
        var groups = await Task.WhenAll(In.GroupBy(x => x.DataOwner).Select(x => x.Key.Instance.GetFiles(x.Select(i => i.ToPluginModel()).ToArray())));
        var tasks = new List<Task>();

        foreach (var torrentFiles in groups) {
            string getCommonPrefix(IEnumerable<string> Input)
            {
                var commonPrefix = Input.First().Split('/');

                foreach (var file in Input) {
                    var s = file.Split('/');

                    int x;
                    for (x = 0;x < Math.Min(commonPrefix.Length, s.Length) && commonPrefix[x] == s[x];x++);

                    commonPrefix = commonPrefix[..x];
                }

                return string.Join('/', commonPrefix);
            }

            var totalSize = (ulong)torrentFiles.Sum(x => x.Value.Files.Sum(f => (decimal)f.Size));
            var count = torrentFiles.Sum(x => x.Value.Files.Count);

            var data = torrentFiles.Select(x => new MoveData(
                Hash: x.Key,
                BasePath: allTorrents[x.Key].RemotePath,
                ContainerFolder: x.Value.MultiFile ? allTorrents[x.Key].Name : "",
                Files: x.Value.Files.Select(f => f.Path)
            )).ToArray();

            var commonPrefix = getCommonPrefix(data.Select(x => x.BasePath));
            var dataProvider = allTorrents[torrentFiles.First().Key].DataOwner;
            var dataProviderName = dataProvider.Config.Plugin.Name;
            var targetDir = await SelectRemoteDirectoryDialog(App.MainWindow, $"RT# - Target directory ({dataProviderName})", dataProvider.Instance, String.IsNullOrEmpty(commonPrefix) ? "/" : commonPrefix);
            if (targetDir == null)
                continue;

            string targetPath(string basePath, MoveData data, string filePath)
            {
                return basePath + (data.ContainerFolder != "" ? "/" + data.ContainerFolder : "") + "/" + filePath;
            }

            var moveConfirmation = await MoveDownloadDirectoryConfirmationDialog(
                App.MainWindow,
                data.SelectMany(x => x.Files.Select(f => targetPath(x.BasePath, x, f))).ToArray(),
                data.SelectMany(x => x.Files.Select(f => targetPath(targetDir, x, f))).ToArray(),
                $"{count} files ({Shared.Utils.Converters.GetSIDataSize(totalSize)})"
            );

            if (!moveConfirmation)
                continue;

            var sourceTargetMapCheck = data.SelectMany(x => x.Files.Select(f => (targetPath(x.BasePath, x, f), targetPath(targetDir, x, f)))).ToArray();

            /*var moveProgress = new Progress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)>(args => {
                var (infoHash, file, moved, additionalProgress) = args;

                float pct;
                string str;

                ulong? size = null;
                if (infoHash?.Length != 0 && torrentFiles.TryGetValue(infoHash, out var files)) {
                    size = files.Files.FirstOrDefault(x => x.Path == file)?.Size;
                }

                if (size == null) {
                    pct = 0;
                    str = file + (additionalProgress != null ? "\n" + additionalProgress : "");
                } else {
                    pct = (float)((double)moved / size * 100);
                    str = file + "..." + (additionalProgress != null ? "\n" + additionalProgress : "");
                }
                
                Log.Logger.Verbose("Move progress: " + pct + "%, " + str);
                ((IProgress<(float, string)>)progress).Report((
                    pct,
                    str
                ));
            });*/

            tasks.Add(Core.ActionQueue.GetActionQueueEntry(dataProvider.Host)!.Queue.RunAction(ActionQueueAction.New("Move torrent", async (action) => {
                var result = await dataProvider.Instance.MoveDownloadDirectory(
                    data.ToInfoHashDictionary(x => x.Hash, _ => targetDir),
                    sourceTargetMapCheck
                );

                if (result != null) {
                    await dataProvider.AttachedDaemonService.GetScriptProgress(result.Value, state => {
                        action.ChangeProgress(state.Progress ?? 0f, state.Text);
                    });
                }
            })));
        }

        if (tasks.Any())
            await Task.WhenAny(tasks);
    }

    public bool CanExecuteRemoveTorrents() => CanExecuteAction("Remove torrent");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteRemoveTorrents))]
    public async Task RemoveTorrents(IReadOnlyList<Torrent> In) 
    {
        In = [.. In]; // cp
        var result = await DeleteTorrentsConfirmationDialog(App.MainWindow, 0UL, In.Count, false);

        if (result)
            await ActionForMulti(In, "Remove torrents", (dp, torrents) => dp.Instance.RemoveTorrents(torrents.Select(x => x.Hash).ToList()));
    }

    public bool CanExecuteRemoveTorrentsAndData() => CanExecuteAction("Remove torrent & data");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteRemoveTorrentsAndData))]
    public async Task RemoveTorrentsAndData(IReadOnlyList<Torrent> In)
    {
        In = [.. In]; // cp
        var serverIds = In.Select(x => x.DataOwner.Config.Plugin.ServerId).Distinct();

        var references = new InfoHashDictionary<Torrent[]>();
        var allTorrents = TorrentPolling.Torrents.GetSnapshot().Where(x => {
            var selectedOnly = In.Where(i => i.Hash.SequenceEqual(x.Hash)).Select(x => x.DataOwner.Config.Plugin.ServerId);
            
            return selectedOnly.Contains(x.DataOwner.Config.Plugin.ServerId);
        }).ToImmutableArray();
        bool multiReference = false;

        foreach (var torrentGroup in allTorrents.GroupBy(x => x.Hash, HashEqualityComparer.Instance)) {
            var found = torrentGroup.GroupBy(x => x.DataOwner.Config.Plugin.ServerId + "_" + x.RemotePath).Where(x => x.Count() > 1);
            foreach (var torrentsInServer in found) {

                if (torrentsInServer.Select(x => x.DataOwner.Host.InstanceId).Except(In.Where(x => x.Hash.SequenceEqual(torrentGroup.Key)).Select(x => x.DataOwner.Host.InstanceId)).Count() != 0) {
                    references[torrentGroup.Key] = torrentsInServer.ToArray();
                    multiReference = true;
                }

            }
        }

        if (multiReference) {
            var msgBox = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                ButtonDefinitions = new[]
                {
                    new ButtonDefinition {
                        Name = "Yes",
                        IsDefault = false
                    },
                    new ButtonDefinition {
                        Name = "No",
                        IsDefault = true,
                        IsCancel = true
                    }
                },
                ContentHeader = "Following torrents are referenced multiple times in same system. Do you really want to remove data of these torrents? This will cause data access errors.",
                ContentMessage = String.Join("\n\n\n\n", references.Select(x => {
                    // TODO: assumes Name always stays the same?
                    return $"**{x.Value[0].Name} ({Convert.ToHexString(x.Value[0].Hash)})**:\n\n" + String.Join("\n", x.Value.Select(x => " * " + x.DataOwner.Config.Plugin.Name + $" ({x.DataOwner.Host.InstanceId}): {x.RemotePath}"));
                })),
                ContentTitle = "RT#",
                Icon = MsBox.Avalonia.Enums.Icon.Warning,
                Markdown = true
            });
            var res = await msgBox.ShowWindowDialogAsync(App.MainWindow);
            if (res == "Yes")
                multiReference = false;
        }

        if (multiReference)
            return;

        var result = await DeleteTorrentsConfirmationDialog(App.MainWindow, (ulong)In.DistinctBy(x => x.DataOwner.Config.Plugin.ServerId + "_" + Convert.ToHexString(x.Hash)).Sum(x => (decimal)x.WantedSize), In.Count, true);

        if (result)
            await ActionForMulti(In, "Remove torrents and data", (dp, torrents) => dp.Instance.RemoveTorrentsAndData([.. torrents.Select(x => x.ToPluginModel())]));
    }

    public bool CanExecuteGetDotTorrents() => CanExecuteAction("Get .torrent");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteGetDotTorrents))]
    public async Task GetDotTorrents(IReadOnlyList<Torrent> In)
    {
        In = [.. In]; // cp
        var result = await SelectDirectoryDialog(App.MainWindow, "Select destination folder");

        if (String.IsNullOrEmpty(result))
            return;

        var tasks = In.GroupBy(x => x.DataOwner).Select(x => (Core.ActionQueue.GetActionQueueEntry(x.Key.Host)!, x.Key.Instance.GetDotTorrents([.. x.Select(i => i.ToPluginModel())])));

        await Task.WhenAll(tasks.Select((data) => {
            return data.Item1.Queue.RunAction(ActionQueueAction.New("Download .torrent files", async (action) => {
                var files = await data.Item2;

                foreach (var (hash, file) in files) {
                    await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(result, Convert.ToHexString(hash) + ".torrent"), file);
                }
            }));
        }));
    }

    public bool CanExecuteEditTorrent() => SelectedItems.Count == 1 && CanExecuteAction("Edit torrent");
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteEditTorrent))]
    public async Task EditTorrent(IReadOnlyList<Torrent> In)
    {
        var torrent = In[0];

        TorrentEditorWindowViewModel viewModel;
        try {
            var dotTorrent = (await torrent.DataOwner.Instance.GetDotTorrents([torrent.ToPluginModel()])).Single().Value;
            viewModel = new TorrentEditorWindowViewModel(torrent, dotTorrent);
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to load .torrent for editing");
            await MessageBoxManager.GetMessageBoxStandard("RT# - Edit torrent", "Failed to load .torrent:\n" + ex.Message, MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Error).ShowWindowAsync();
            return;
        }

        await new TorrentEditorWindow {
            ViewModel = viewModel
        }.ShowDialog(App.MainWindow);
    }

    public bool CanExecuteAddLabel() => CanExecuteAction("Add label");
    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteAddLabel))]
    private async Task AddLabel((object SelectedItems, string Text) In)
    {
        var selectedItems = ((IReadOnlyList<Torrent>)In.SelectedItems).ToArray();
        try {
            await ActionForMulti(selectedItems, "Set labels", (dp, torrents) =>
                dp.Instance.SetLabels(torrents
                    .Where(x => !x.Labels.Contains(In.Text))
                    .Select(torrent => (
                        torrent.Hash,
                        torrent.Labels.Append(In.Text).ToArray()
                    )).ToList())
            );
        } finally {
            CloseAddLabelDialog();
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteAddLabel))]
    private async Task ToggleLabel((Torrent[] SelectedItems, string Text, bool CurrentValue) In)
    {
        await ActionForMulti(In.SelectedItems.ToArray(), "Set labels", (dp, torrents) =>
            dp.Instance.SetLabels(torrents
                .Select(torrent => {
                    string[] labels;

                    if (In.CurrentValue) {
                        // Now remove
                        labels = torrent.Labels.Where(x => x != In.Text).ToArray();
                    } else {
                        // Now add
                        if (!torrent.Labels.Contains(In.Text))
                            labels = torrent.Labels.Append(In.Text).ToArray();
                        else
                            labels = torrent.Labels.ToArray();
                    }

                    return (torrent.Hash, labels);
                }).ToList()
            )
        );
    }

    public bool CanExecuteDuplicateTorrentTo() => CanExecuteAction("Duplicate torrent to");
    [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteDuplicateTorrentTo))]
    private async Task DuplicateTorrentTo(IReadOnlyList<Torrent> In)
    {
        In = [ ..In ]; // cp

        foreach (var torrent in In) {
            var source = torrent.ToPluginModel();

            var target = await Core.TorrentDuplication.SelectTarget(App.MainWindow, source, x => x.Host.Config.Plugin.InstanceId != source.DataOwner.Host.Config.Plugin.InstanceId);
            if (target == null)
                return;

            Core.TorrentDuplication.QueueSingle(source, [ target ]);
        }
    }
}
