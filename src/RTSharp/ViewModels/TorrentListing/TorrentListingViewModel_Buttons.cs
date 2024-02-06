﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Collections;
using RTSharp.Shared.Abstractions;
using Torrent = RTSharp.Models.Torrent;
using System.Reactive.Linq;
using System.Windows.Input;
using Avalonia.Controls;
using RTSharp.Shared.Utils;
using Serilog;
using CommunityToolkit.Mvvm.Input;
using System.Diagnostics;
using static SkiaSharp.HarfBuzz.SKShaper;
using RTSharp.Views;
using RTSharp.Plugin;
using System.Text.RegularExpressions;
using System.Security.Policy;
using RTSharp.Core.TorrentPolling;
using System.Collections.Immutable;
using MsBox.Avalonia;
using MsBox.Avalonia.Models;
using RTSharp.Core.Services.Auxiliary;

namespace RTSharp.ViewModels.TorrentListing
{
    public partial class TorrentListingViewModel
    {
        public Func<(Window Owner, ulong Size, int Count), Task<bool>> RecheckTorrentsConfirmationDialog { get; set; }

        public Func<(Window Owner, string[] CurrentFiles, string[] FutureFiles, string MoveWarning), Task<bool>> MoveDownloadDirectoryConfirmationDialog { get; set; }

        public Func<(Window Owner, string Title), Task<string?>> SelectDirectoryDialog { get; set; }

        public Func<(Window Owner, string Title, Plugin.DataProvider DataProvider, string? StartingDir), Task<string?>> SelectRemoteDirectoryDialog { get; set; }

        public Func<(Window Owner, ulong Size, int Count, bool AndData), Task<bool>> DeleteTorrentsConfirmationDialog { get; set; }

        public Action ShowAddLabelDialog { get; set; }

        public Action CloseAddLabelDialog { get; set; }

        private async Task ActionForMulti(IList In, string ActionName, Func<IDataProvider, IList<Torrent>, Task<IList<(byte[] Hash, IList<Exception> Exceptions)>>> Fx)
        {
            var torrents = In.Cast<Torrent>();
            try {
                var actions = torrents.GroupBy(x => x.Owner).Select(x => (
                    Core.ActionQueue.GetActionQueueEntry(x.Key.PluginInstance),
                    ActionQueueAction.New(ActionName, async () => {
                        var result = await Fx(x.Key.Instance, x.ToList());
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

        bool CanExecuteAction(string Action)
        {
            var dps = CurrentlySelectedItems.Cast<Torrent>().GroupBy(x => x.Owner).DistinctBy(x => x.Key.PluginInstance.Instance.GUID).Select(x => x.Key);
            bool startTorrentCap = dps.All(x => x.Instance.Capabilities.StartTorrent);
            bool pauseTorrentCap = dps.All(x => x.Instance.Capabilities.PauseTorrent);
            bool stopTorrentCap = dps.All(x => x.Instance.Capabilities.StopTorrent);

            var strToCap = new Dictionary<string, Func<IDataProvider, bool>>() {
                { "Start", x => x.Capabilities.StartTorrent },
                { "Pause", x => x.Capabilities.PauseTorrent },
                { "Stop", x => x.Capabilities.StopTorrent },
                { "Force recheck", x => x.Capabilities.ForceRecheckTorrent },
                { "Reannounce to all trackers", x => x.Capabilities.ReannounceToAllTrackers },
                { "Move download directory", x => x.Capabilities.MoveDownloadDirectory },
                { "Remove torrent", x => x.Capabilities.RemoveTorrent },
                { "Remove torrent & data", x => x.Capabilities.RemoveTorrentAndData },
                { "Get .torrent", x => x.Files.Capabilities.GetDotTorrents },
                { "Add label", x => x.Capabilities.AddLabel },
                { "Duplicate torrent to", x => x.Capabilities.AddTorrent && x.Capabilities.ForceRecheckTorrent && x.Capabilities.StartTorrent }
            };

            Debug.Assert(strToCap.ContainsKey(Action));

            if (CurrentlySelectedItems.Count == 1) {
                var currentlySelectedTorrent = (Torrent)CurrentlySelectedItems[0]!;
                return Action switch {
                    "Start" => currentlySelectedTorrent.InternalState != TORRENT_STATE.DOWNLOADING &&
                                currentlySelectedTorrent.InternalState != TORRENT_STATE.SEEDING &&
                                currentlySelectedTorrent.InternalState != TORRENT_STATE.HASHING,
                    "Pause" => currentlySelectedTorrent.InternalState != TORRENT_STATE.PAUSED &&
                                currentlySelectedTorrent.InternalState != TORRENT_STATE.STOPPED &&
                                currentlySelectedTorrent.InternalState != TORRENT_STATE.COMPLETE,
                    "Stop" => currentlySelectedTorrent.InternalState != TORRENT_STATE.STOPPED,
                    _ => true
                } && strToCap[Action](currentlySelectedTorrent.Owner.Instance);
            } else
                return dps.All(x => strToCap[Action](x.Instance));
        }

        public bool CanExecuteStartTorrents() => CanExecuteAction("Start");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteStartTorrents))]
        public async Task StartTorrents(IList In) => await ActionForMulti(In, "Start torrents", (dp, torrents) => dp.StartTorrents(torrents.Select(x => x.Hash).ToList()));

        public bool CanExecutePauseTorrents() => CanExecuteAction("Pause");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecutePauseTorrents))]
        public async Task PauseTorrents(IList In) => await ActionForMulti(In, "Pause torrents", (dp, torrents) => dp.PauseTorrents(torrents.Select(x => x.Hash).ToList()));

        public bool CanExecuteStopTorrents() => CanExecuteAction("Stop");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteStopTorrents))]
        public async Task StopTorrents(IList In) => await ActionForMulti(In, "Stop torrents", (dp, torrents) => dp.StopTorrents(torrents.Select(x => x.Hash).ToList()));

        public bool CanExecuteForceRecheckTorrents() => CanExecuteAction("Force recheck");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteForceRecheckTorrents))]
        public async Task ForceRecheckTorrents(IList In)
        {
            var torrents = In.Cast<Torrent>();
            var result = await RecheckTorrentsConfirmationDialog((App.MainWindow, (ulong)torrents.Sum(x => (decimal)x.WantedSize), In.Count));

            if (result)
                await ActionForMulti(In, "Force recheck", (dp, torrents) => dp.ForceRecheck(torrents.Select(x => x.Hash).ToList()));
        }

        public bool CanExecuteReannounceToAllTrackers() => CanExecuteAction("Reannounce to all trackers");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteReannounceToAllTrackers))]
        public async Task ReannounceToAllTrackers(IList In) => await ActionForMulti(In, "Reannounce to all trackers", (dp, torrents) => dp.ReannounceToAllTrackers(torrents.Select(x => x.Hash).ToList()));

        record MoveData(byte[] Hash, string BasePath, string ContainerFolder, IEnumerable<string> Files);

        public bool CanExecuteMoveDownloadDirectory() => CanExecuteAction("Move download directory");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteMoveDownloadDirectory))]
        public async Task MoveDownloadDirectory(IList In)
        {
            var allTorrents = In.Cast<Torrent>().ToInfoHashDictionary(x => x.Hash);
            var groups = await Task.WhenAll(In.Cast<Torrent>().GroupBy(x => x.Owner).Select(x => x.Key.Instance.GetFiles(x.Select(i => i.ToPluginModel()).ToArray())));
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
                var dataProvider = allTorrents[torrentFiles.First().Key].Owner;
                var dataProviderName = dataProvider.PluginInstance.PluginInstanceConfig.Name;
                var targetDir = await SelectRemoteDirectoryDialog((App.MainWindow, $"RT# - Target directory ({dataProviderName})", dataProvider, String.IsNullOrEmpty(commonPrefix) ? "/" : commonPrefix));
                if (targetDir == null)
                    continue;

                string targetPath(string basePath, MoveData data, string filePath)
                {
                    return basePath + (data.ContainerFolder != "" ? "/" + data.ContainerFolder : "") + "/" + filePath;
                }

                var moveConfirmation = await MoveDownloadDirectoryConfirmationDialog((
                    App.MainWindow,
                    data.SelectMany(x => x.Files.Select(f => targetPath(x.BasePath, x, f))).ToArray(),
                    data.SelectMany(x => x.Files.Select(f => targetPath(targetDir, x, f))).ToArray(),
                    $"{count} files ({Shared.Utils.Converters.GetSIDataSize(totalSize)})"
                ));

                if (!moveConfirmation)
                    continue;

                var sourceTargetMapCheck = data.SelectMany(x => x.Files.Select(f => (targetPath(x.BasePath, x, f), targetPath(targetDir, x, f)))).ToArray();

                var progress = new Progress<(float, string)>();
                var moveProgress = new Progress<(byte[] InfoHash, string File, ulong Moved, string? AdditionalProgress)>(args => {
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
                });

                tasks.Add(Core.ActionQueue.GetActionQueueEntry(dataProvider.PluginInstance)!.Queue.RunAction(ActionQueueAction.New("Move torrent", async () => {
                    var result = await dataProvider.Instance.MoveDownloadDirectory(
                        data.Select(x => (x.Hash, targetDir)).ToList(),
                        sourceTargetMapCheck,
                        moveProgress
                    );

                    if (result.Any(x => x.Exceptions.Any())) {
                        throw new Exception("Operation failed:\n" + String.Join('\n', result.Select(x => Convert.ToHexString(x.Hash) + ": " + String.Join(", ", x.Exceptions.Select(x => x.Message)))));
                    }
                }, progress)));
            }

            if (tasks.Any())
                await Task.WhenAny(tasks);
        }

        public bool CanExecuteRemoveTorrents() => CanExecuteAction("Remove torrent");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteRemoveTorrents))]
        public async Task RemoveTorrents(IList In) 
        {
            var torrents = In.Cast<Torrent>();
            var result = await DeleteTorrentsConfirmationDialog((App.MainWindow, 0UL, In.Count, false));

            if (result)
                await ActionForMulti(In, "Remove torrents", (dp, torrents) => dp.RemoveTorrents(torrents.Select(x => x.Hash).ToList()));
        }

        public bool CanExecuteRemoveTorrentsAndData() => CanExecuteAction("Remove torrent & data");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteRemoveTorrentsAndData))]
        public async Task RemoveTorrentsAndData(IList In)
        {
            var torrents = In.Cast<Torrent>();
            var serverIds = torrents.Select(x => x.Owner.PluginInstance.PluginInstanceConfig.ServerId).Distinct();

            var references = new InfoHashDictionary<Torrent[]>();
            var allTorrents = TorrentPolling.Torrents.Where(x => {
                var selectedOnly = torrents.Where(i => i.Hash.SequenceEqual(x.Hash)).Select(x => x.Owner.PluginInstance.PluginInstanceConfig.ServerId);
                
                return selectedOnly.Contains(x.Owner.PluginInstance.PluginInstanceConfig.ServerId);
            }).ToImmutableArray();
            bool multiReference = false;

            foreach (var torrentGroup in allTorrents.GroupBy(x => x.Hash, new HashEqualityComparer())) {
                var found = torrentGroup.GroupBy(x => x.Owner.PluginInstance.PluginInstanceConfig.ServerId + "_" + x.RemotePath).Where(x => x.Count() > 1);
                foreach (var torrentsInServer in found) {

                    if (torrentsInServer.Select(x => x.Owner.PluginInstance.InstanceId).Except(torrents.Where(x => x.Hash.SequenceEqual(torrentGroup.Key)).Select(x => x.Owner.PluginInstance.InstanceId)).Count() != 0) {
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
                        return $"**{x.Value[0].Name} ({Convert.ToHexString(x.Value[0].Hash)})**:\n\n" + String.Join("\n", x.Value.Select(x => " * " + x.Owner.PluginInstance.PluginInstanceConfig.Name + $" ({x.Owner.PluginInstance.InstanceId}): {x.RemotePath}"));
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

            var result = await DeleteTorrentsConfirmationDialog((App.MainWindow, (ulong)torrents.DistinctBy(x => x.Owner.PluginInstance.PluginInstanceConfig.ServerId + "_" + Convert.ToHexString(x.Hash)).Sum(x => (decimal)x.WantedSize), In.Count, true));

            if (result)
                await ActionForMulti(In, "Remove torrents and data", (dp, torrents) => dp.RemoveTorrentsAndData(torrents.Select(x => x.Hash).ToList()));
        }

        public bool CanExecuteGetDotTorrents() => CanExecuteAction("Get .torrent");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteGetDotTorrents))]
        public async Task GetDotTorrents(IList In)
        {
            var torrents = In.Cast<Torrent>();

            var result = await SelectDirectoryDialog((App.MainWindow, "Select destination folder"));

            if (String.IsNullOrEmpty(result))
                return;

            var tasks = torrents.GroupBy(x => x.Owner).Select(x => (Core.ActionQueue.GetActionQueueEntry(x.Key.PluginInstance), x.Key.Instance.GetDotTorrents(x.Select(i => i.Hash).ToList())));

            await Task.WhenAll(tasks.Select((data) => {
                var action = ActionQueueAction.New("Download .torrent files", async () => {
                    var files = await data.Item2;

                    foreach (var (hash, file) in files) {
                        await System.IO.File.WriteAllBytesAsync(System.IO.Path.Combine(result, Convert.ToHexString(hash) + ".torrent"), file);
                    }
                });
                return data.Item1.Queue.RunAction(action);
            }));
        }

        public bool CanExecuteAddLabel() => CanExecuteAction("Add label");
        [RelayCommand(AllowConcurrentExecutions = true, CanExecute = nameof(CanExecuteAddLabel))]
        private async Task AddLabel((IList SelectedItems, string Text) In)
        {
            try {
                await ActionForMulti(In.SelectedItems, "Set labels", (dp, torrents) =>
                    dp.SetLabels(torrents
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
            await ActionForMulti(In.SelectedItems, "Set labels", (dp, torrents) =>
                dp.SetLabels(torrents
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
                    }).ToList())
            );
        }

        public bool CanExecuteDuplicateTorrentTo() => CanExecuteAction("Duplicate torrent to");
        [RelayCommand(AllowConcurrentExecutions = false, CanExecute = nameof(CanExecuteDuplicateTorrentTo))]
        private async Task DuplicateTorrentTo(IList In)
        {
            var torrents = In.Cast<Torrent>();

            Log.Logger.Information("Torrents: ");
            foreach (var t in torrents) {
                Log.Logger.Information("t: " + Convert.ToHexString(t.Hash));
            }

            var dests = new InfoHashDictionary<(DataProvider Provider, string Directory)>();

            foreach (var torrent in torrents) {
                var wnd = new TorrentDuplicationTargetSelectorWindow {
                    ViewModel = new TorrentDuplicationTargetSelectorWindowViewModel(torrent)
                };

                var result = await wnd.ShowDialog<bool>(App.MainWindow);
                if (!result)
                    return;

                dests[torrent.Hash] = (wnd.ViewModel.SelectedProvider, wnd.ViewModel.RemoteTargetPath);
            }

            var tasks = dests.GroupBy(x => x.Value.Provider).Select(x => (
                ActionQueue: Core.ActionQueue.GetActionQueueEntry(x.Key.PluginInstance),
                Hashes: x.Select(i => i.Key).ToList(),
                DataProvider: x.Key.Instance
            ));

            var exec = tasks.Select((data) => {
                var targetDataProvider = data.DataProvider;

                var getDotTorrentFiles = ActionQueueAction.New("Download .torrent files", () => {
                    var groups = torrents.GroupBy(x => x.Owner);
                    var tasks = groups.Select(x => x.Key.Instance.GetDotTorrents(x.Select(x => x.Hash).ToArray()));
                    return Task.WhenAll(tasks);
                });

                var getTorrentFileList = getDotTorrentFiles.CreateChild("Get torrent file list", RUN_MODE.PARALLEL_DONT_WAIT_ON_PARENT, (parent) => {
                    var groups = torrents.GroupBy(x => x.Owner);
                    var tasks = groups.Select(x => x.Key.Instance.GetFiles(x.Select(x => x.ToPluginModel()).ToArray()));
                    return Task.WhenAll(tasks);
                });

                var addTorrents = getDotTorrentFiles.CreateChild("Add torrents", RUN_MODE.DEPENDS_ON_PARENT, (parent) => {
                    var dotTorrents = parent.GetResult()!.SelectMany(x => x).ToInfoHashDictionary(x => x.Key, x => x.Value);

                    return targetDataProvider.AddTorrents(dotTorrents.Select(x => (
                        Data: x.Value,
                        Filename: (string?)torrents.First(i => i.Hash.SequenceEqual(x.Key)).Name,
                        Options: new AddTorrentsOptions(null, dests[x.Key].Directory)
                    )).ToArray());
                });

                ActionQueueAction<InfoHashDictionary<bool>> transferFiles = null;
                transferFiles = addTorrents.CreateChild("Transfer files", RUN_MODE.DEPENDS_ON_PARENT, async (parent) => {
                    var res = parent.GetResult()!;

                    await Task.Delay(5000); // TODO: AddTorrents should ensure torrent exists

                    var ret = new InfoHashDictionary<bool>();

                    foreach (var (hash, exceptions) in res) {
                        if (exceptions?.Any() == true)
                            continue;

                        Log.Logger.Information("Torrents: ");
                        foreach (var t in torrents) {
                            Log.Logger.Information("t: " + Convert.ToHexString(t.Hash));
                        }
                        var sourceTorrent = torrents.First(x => x.Hash.SequenceEqual(hash));

                        if (sourceTorrent.Owner.PluginInstance.PluginInstanceConfig.ServerId != targetDataProvider.PluginHost.PluginInstanceConfig.ServerId) {
                            await getTorrentFileList.RunningTask;
                            var fileList = getTorrentFileList.GetResult()!.SelectMany(x => x).ToInfoHashDictionary(x => x.Key, x => x.Value);
                            var files = fileList[sourceTorrent.Hash];
                            var dest = dests[sourceTorrent.Hash].Directory;

                            var auxiliarySource = new AuxiliaryService(sourceTorrent.Owner.PluginInstance.PluginInstanceConfig.ServerId);
                            var auxiliaryTarget = new AuxiliaryService(targetDataProvider.PluginHost.PluginInstanceConfig.ServerId);

                            var progressRaw = new Progress<(float, string)>();
                            IProgress<(float, string)> progress = progressRaw;
                            transferFiles!.BindProgress(progressRaw);

                            try {
                                await auxiliaryTarget.RequestRecieveFiles(
                                    files.Files.Select(x => (
                                        RemoteSource: sourceTorrent.RemotePath + (files.MultiFile ? ("/" + sourceTorrent.Name) : "") + "/" + x.Path,
                                        StoreTo: dest + (files.MultiFile ? ("/" + sourceTorrent.Name) : "") + "/" + x.Path
                                    )),
                                    sourceTorrent.Owner.PluginInstance.PluginInstanceConfig.ServerId,
                                    new Progress<(string File, ulong BytesTransferred)>((info) => {
                                        var torrentFile = files.Files.First(x => info.File.EndsWith(x.Path)); // TODO: match more accurately?
                                        var pctDone = (float)info.BytesTransferred / torrentFile.Size * 100f;

                                        progress.Report((pctDone, $"{info.File}"));
                                    })
                                );
                            } catch (Exception ex) {
                                Log.Logger.Error(ex, $"ReceiveFiles failed for {Convert.ToHexString(hash)}");
                                ret[hash] = false;
                                continue;
                            }
                        }
                        ret[hash] = true;
                    }

                    return ret;
                });

                transferFiles.CreateChild("Force recheck", RUN_MODE.DEPENDS_ON_PARENT, async (parent) => {
                    var res = parent.GetResult()!;

                    return targetDataProvider.ForceRecheck(res.Where(x => x.Value).Select(x => x.Key).ToArray());
                });
                
                return data.ActionQueue!.Queue.RunAction(getDotTorrentFiles);
            });

            await Task.WhenAll(exec);
        }
    }
}
