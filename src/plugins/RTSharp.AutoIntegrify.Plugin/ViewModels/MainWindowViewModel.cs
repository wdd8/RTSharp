using Avalonia.Threading;

using BencodeNET.Parsing;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Models;

using RTSharp.Scripts;
using RTSharp.Scripts.SolveFile;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Controls;
using RTSharp.Shared.Utils;

using System.Collections.ObjectModel;
using System.Diagnostics;

using static RTSharp.Shared.Utils.BencodeUtils;

namespace RTSharp.AutoIntegrify.Plugin.ViewModels;

public enum RESOLVE_STATE
{
    PENDING,
    FILE_TOO_SMALL,
    UNRESOLVED,
    RESOLVED,
    RESOLVED_HEURISTICS
}

public partial class FileInfo : ObservableObject
{
    public required TorrentFile File { get; init; }

    public required BencodeUtils.PieceHash[] PieceHashes { get; init; }

    public Range PieceRange { get; init; }

    public required FileBlockHashAlgorithm HashAlgorithm { get; init; }

    [ObservableProperty]
    public partial string? ResolvedPath { get; set; }

    [ObservableProperty]
    public partial BuiltInIcons? StatusIcon { get; set; }

    public RESOLVE_STATE Resolved {
        get {
            return field;
        }
        set {
            field = value;
            StatusIcon = field switch {
                RESOLVE_STATE.PENDING => BuiltInIcons.VISTA_WAIT,
                RESOLVE_STATE.FILE_TOO_SMALL => BuiltInIcons.VISTA_BLOCK,
                RESOLVE_STATE.UNRESOLVED => BuiltInIcons.SERIOUS_QUESTION,
                RESOLVE_STATE.RESOLVED => BuiltInIcons.VISTA_OK,
                RESOLVE_STATE.RESOLVED_HEURISTICS => BuiltInIcons.VISTA_OK_EXCLAMATION,
                _ => null
            };
        }
    }
}

public partial class MainWindowViewModel(IPluginHost<AutoIntegrifyPluginConfig> Host, MainWindow Window, Torrent Torrent) : ObservableViewModel
{
    [ObservableProperty]
    public partial ObservableCollection<FileInfo> Files { get; set; } = new();

    [ObservableProperty]
    public partial FileInfo SelectedFile { get; set; }

    [ObservableProperty]
    public partial List<PieceState> Pieces { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "Loading...";

    [ObservableProperty]
    public partial bool ProgressDialogShown { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "There are files still unresolved";

    [ObservableProperty]
    public partial string ActionButtonText { get; set; } = "Perform pending hardlinks";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public bool canExecuteCancel = true;

    private volatile CancellationTokenSource Cancellation = new();

    private bool IsMultiFile;

    Dictionary<string, string> PendingLinks = [];

    List<string> AdditionalPaths = null!; // OnContextPopulated
    int RandomPiecesToCheck;
    int SearchRecursionLimit;
    bool Reflink;
    bool Hardlink;

    public override void OnContextPopulated()
    {
        AdditionalPaths = Host.Config.SearchPaths;
        RandomPiecesToCheck = Host.Config.RandomPiecesToCheck;
        SearchRecursionLimit = Host.Config.SearchRecursionLimit;
        var linkOptions = Host.Config.LinkOptions;
        if (linkOptions == LinkOptions.ReflinkWithHardlinkFallback) {
            Reflink = true;
            Hardlink = true;
        } else if (linkOptions == LinkOptions.Reflink) {
            Reflink = true;
            Hardlink = false;
        } else {
            Reflink = false;
            Hardlink = true;
        }

        if (AdditionalPaths.Count == 0) {
            var msgbox = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                ButtonDefinitions = [ new ButtonDefinition() { Name = "OK", IsDefault = true } ],
                ImageIcon = BuiltInIcon.Get(BuiltInIcons.DID_YOU_KNOW),
                WindowIcon = new Avalonia.Controls.WindowIcon(BuiltInIcon.Get(BuiltInIcons.RTSHARP)),
                ContentTitle = "Auto integrify",
                ContentHeader = "Did you know...",
                ContentMessage = "You can add additional search paths in plugin settings.\nBy default, only torrent current remote path is searched."
            });
            _ = Dispatcher.UIThread.InvokeAsync(async () => {
                await msgbox.ShowWindowDialogAsync(Window);
            });
        }

        _ = Task.Run(async () => {
            Cancellation = new();

            Dispatcher.UIThread.Invoke(() => {
                ProgressDialogShown = true;
                ProgressText = "Processing torrent pieces...";
            });

            await Integrify();
        });
    }

    private async Task Integrify()
    {
        Files.Clear();
        PendingLinks = [];

        Dispatcher.UIThread.Invoke(() => {
            ProgressDialogShown = true;
            CanExecuteCancel = true;
        });

        try {
            if (Torrent.Done >= 100) {
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = "Torrent integrity is verified";
                    CanExecuteCancel = false;
                });
                return;
            }

            await AssignHashesToPieces();

            foreach (var file in Files) {
                if (Cancellation.IsCancellationRequested)
                    return;

                for (var x = 0;x < 2;x++) {
                    await SolveFile(file, x == 0 ? false : true);

                    if (file.Resolved == RESOLVE_STATE.RESOLVED || file.Resolved == RESOLVE_STATE.RESOLVED_HEURISTICS) {
                        var shouldBeAtPath = IsMultiFile ? string.Join('/', Torrent.RemotePath, Torrent.Name, file.File.Path) : string.Join('/', Torrent.RemotePath, file.File.Path);

                        if (shouldBeAtPath != file.ResolvedPath) {
                            PendingLinks.Add(file.ResolvedPath!, shouldBeAtPath);
                        }
                        break;
                    }
                }
            }

            if (Files.Any(x => x.Resolved != RESOLVE_STATE.RESOLVED && x.Resolved != RESOLVE_STATE.RESOLVED_HEURISTICS)) {
                Dispatcher.UIThread.Invoke(() => {
                    StatusText = "Failed to find files";
                    ActionButtonText = "Retry";
                });
            } else if (PendingLinks.Count == 0) {
                Dispatcher.UIThread.Invoke(() => {
                    StatusText = "All files resolved";
                    ActionButtonText = "Force recheck";
                });
            } else {
                Dispatcher.UIThread.Invoke(() => {
                    StatusText = $"Ready to perform {PendingLinks.Count} hardlink{(PendingLinks.Count == 1 ? "" : "s")}";
                    ActionButtonText = "Perform pending hardlinks";
                });
            }

            Dispatcher.UIThread.Invoke(() => {
                ProgressText = "Done";
                ProgressDialogShown = false;
            });
        } catch (Exception ex) {
            Host.Logger.Error(ex, "Error solving files");
            Dispatcher.UIThread.Invoke(() => {
                ProgressText = "Error";
                CanExecuteCancel = false;
            });
        }
    }

    private async Task<bool> SolveFile(FileInfo File, bool WideSearch)
    {
        Dispatcher.UIThread.Invoke(() => {
            ProgressText = $"Searching for {File.File.Path}{(WideSearch ? " (wide search)" : "")}...";
        });

        var remotePathParts = Torrent.RemotePath.Split('/');

        var script = await Torrent.DataOwner.Host.AttachedDaemonService!.RunCustomScript(
            DaemonScripts.SolveFile, "AutoIntegrify", [ScriptMessage.Of(new SolveFileInput(
                AdditionalPaths: [.. AdditionalPaths],
                FilePath: File.File.Path,
                Size: (long)File.File.FileSize,
                CurrentPath: IsMultiFile ? $"{Torrent.RemotePath}/{Torrent.Name}" : Torrent.RemotePath,
                LimitPath: string.Join('/', remotePathParts[..^1]),
                IsMultiFile: IsMultiFile,
                WideSearch: WideSearch,
                RecurseLimit: SearchRecursionLimit
            ))]);

        Cancellation.Token.Register(() => {
            _ = Torrent?.DataOwner?.Host?.AttachedDaemonService?.QueueScriptCancellation(script);
        });

        Dispatcher.UIThread.Invoke(() => {
            ProgressText = $"Searching for {File.File.Path}: Script starting";
        });

        await using var channel = Torrent.DataOwner.Host.AttachedDaemonService.OpenScriptChannel(script, Cancellation.Token);

        var result = channel.Read<SolveFileResult>(Cancellation.Token).AsTask();

        await Torrent.DataOwner.Host.AttachedDaemonService.GetScriptProgress(script, progress => {
            Dispatcher.UIThread.Invoke(() => {
                ProgressText = progress.State == TASK_STATE.DONE
                    ? $"Searching for {File.File.Path}{(WideSearch ? " (wide search)" : "")}: Done"
                    : $"Searching for {File.File.Path}{(WideSearch ? " (wide search)" : "")}: {progress.Text}";
            });
        });

        List<string> paths = [.. (await result)?.Paths ?? []];

        Dispatcher.UIThread.Invoke(() => {
            ProgressText = $"Searching for {File.File.Path}: Script end";
        });

        if (paths.Count == 0) {
            Dispatcher.UIThread.Invoke(() => {
                ProgressText = $"No files found for {File.File.Path}";
                File.Resolved = RESOLVE_STATE.UNRESOLVED;
                RefreshPieces(File, SelectedFile);
            });
            return false;
        }

        if (paths.Count > 20) {
            paths = paths[..20];
        }

        Dispatcher.UIThread.Invoke(() => {
            ProgressText = $"{File.File.Path}: Verifying random pieces...";
        });

        foreach (var path in paths) {
            var info = await Torrent.DataOwner.Host.AttachedDaemonService.GetDirectoryInfo(path);
            if (info.Size == (ulong)File.File.FileSize) {
                if (File.PieceHashes.Length != 0) {
                    var verifyPieces = await VerifyPieces(File, path);
                    if (verifyPieces) {
                        File.ResolvedPath = path;
                        File.Resolved = RESOLVE_STATE.RESOLVED;
                        try {
                            RefreshPieces(File, SelectedFile);
                        } catch {
                            Debugger.Break();
                        }
                        return true;
                    }
                } else {
                    File.ResolvedPath = path;
                    File.Resolved = RESOLVE_STATE.RESOLVED_HEURISTICS;
                    RefreshPieces(File, SelectedFile);
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<bool> VerifyPieces(FileInfo file, string resolvedPath)
    {
        var startPiece = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, file.PieceHashes[0].Start, file.PieceHashes[0].End, file.HashAlgorithm);
        if (!startPiece.SequenceEqual(file.PieceHashes[0].Hash)) {
            return false;
        }

        if (file.PieceHashes.Length > 1) {
            var lastPiece = file.PieceHashes.Last();
            var hash = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, lastPiece.Start, lastPiece.End, file.HashAlgorithm);
            if (!hash.SequenceEqual(lastPiece.Hash)) {
                return false;
            }
        }

        for (var x = 0; x < Math.Min(RandomPiecesToCheck, file.PieceHashes.Length - 2);x++) {
            var piece = file.PieceHashes[Random.Shared.Next(1, file.PieceHashes.Length - 2)];
            var hash = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, piece.Start, piece.End, file.HashAlgorithm);
            if (!hash.SequenceEqual(piece.Hash)) {
                return false;
            }
        }

        return true;
    }

    private async Task AssignHashesToPieces()
    {
        var dotTorrent = await Torrent.DataOwner.GetDotTorrents([ Torrent ]);

        if (dotTorrent.Single().Value.Length == 9 && Torrent.DataOwner.GUID == new Guid("E347109B-A06D-4894-9E3A-6FFF63411370") && dotTorrent.Single().Value.SequenceEqual("Not Found"u8)) {
            var msgbox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.Ok,
                ContentTitle = "RT# - AutoIntegrify",
                Icon = MsBox.Avalonia.Enums.Icon.Database,
                WindowStartupLocation = Avalonia.Controls.WindowStartupLocation.CenterOwner,
                ContentMessage = "You have most likely encountered a qBittorrent bug, which can make your torrent unrecheckable. To fix this, please remove the torrent, readd, and force recheck it yourself.\n\nAuto integrify process will now fail."
            });
            _ = Dispatcher.UIThread.InvokeAsync(async () => {
                await msgbox.ShowWindowDialogAsync(Window);
            });
        }

        using var mem = new System.IO.MemoryStream(dotTorrent.Single().Value);

        var bTorrent = new BencodeParser().Parse<BencodeNET.Objects.BDictionary>(mem);
        var info = bTorrent.Get<BencodeNET.Objects.BDictionary>("info")
            ?? throw new System.IO.InvalidDataException("Torrent has no \"info\" dictionary");

        var pieces = new List<PieceState>();

        FileInfo[] files;
        int totalPieces;

        if (!info.ContainsKey("file tree")) {
            IsMultiFile = info.ContainsKey("files");

            files = [.. BencodeUtils.AssignHashesToPieces(bTorrent).Select(x => new FileInfo {
                File = x.File,
                PieceHashes = x.PieceHashes,
                PieceRange = x.PieceRange,
                HashAlgorithm = new FileBlockHashAlgorithm.Sha1()
            })];

            totalPieces = (int)Math.Ceiling(files.Sum(x => x.File.FileSize) / (float)BencodeUtils.GetPieceLength(info));
        } else {
            files = [.. BencodeUtils.AssignHashesToPiecesV2(bTorrent).Select(x => new FileInfo {
                File = x.File,
                PieceHashes = x.PieceHashes,
                PieceRange = x.PieceRange,
                HashAlgorithm = new FileBlockHashAlgorithm.Sha256BitTorrentV2Merkle(x.MerkleHeight)
            })];

            IsMultiFile = files.Length > 1 || files[0].File.Path != BencodeUtils.GetName(info);

            totalPieces = files[^1].PieceRange.End.Value;
        }

        foreach (var file in files) {
            if (file.PieceHashes.Length == 0) {
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = $"{file.File}: File too small";
                    file.Resolved = RESOLVE_STATE.FILE_TOO_SMALL;
                    Files.Add(file);
                });
                continue;
            }

            file.Resolved = RESOLVE_STATE.PENDING;
            Files.Add(file);
        }

        pieces.AddRange(Enumerable.Repeat(PieceState.NotDownloaded, totalPieces));

        Dispatcher.UIThread.Invoke(() => {
            Pieces = pieces;
        });
    }

    [RelayCommand]
    public async Task ActionButton()
    {
        if (PendingLinks.Count != 0) {
            await Torrent.DataOwner.Host.AttachedDaemonService!.LinkFiles([.. PendingLinks], Reflink, Hardlink);
        } else if (Files.All(x => x.Resolved == RESOLVE_STATE.RESOLVED || x.Resolved == RESOLVE_STATE.RESOLVED_HEURISTICS)) {
            var id = await Torrent.DataOwner.ForceRecheck([ Torrent.Hash ]);

            Dispatcher.UIThread.Invoke(() => {
                ProgressText = $"Waiting for recheck...";
                ProgressDialogShown = true;
            });

            await Torrent.DataOwner.Host.AttachedDaemonService!.GetScriptProgress(id, null);

            Torrent = await Torrent.DataOwner.GetTorrent(Torrent.Hash);

            Dispatcher.UIThread.Invoke(() => {
                ProgressText = $"Done";
                ProgressDialogShown = false;
            });
        }

        await Integrify();
    }

    partial void OnSelectedFileChanging(FileInfo oldValue, FileInfo newValue)
    {
        if (oldValue != null && oldValue.PieceHashes.Length != 0) {
            RefreshPieces(oldValue, newValue);
        } else {
            RefreshPieces(newValue, newValue);
        }
    }

    private void RefreshPieces(FileInfo file, FileInfo? selected)
    {
        if (file != selected) {
            for (var x = file.PieceRange.Start.Value;x < file.PieceRange.End.Value;x++) {
                Pieces[x] = file.Resolved switch {
                    RESOLVE_STATE.PENDING => PieceState.Downloading,
                    RESOLVE_STATE.FILE_TOO_SMALL => PieceState.NotDownloaded,
                    RESOLVE_STATE.UNRESOLVED => PieceState.NotDownloaded,
                    RESOLVE_STATE.RESOLVED => PieceState.Downloaded,
                    RESOLVE_STATE.RESOLVED_HEURISTICS => PieceState.Downloaded,
                    _ => throw new Exception("Invalid resolve state")
                };
            }
        }

        if (selected != null) {
            for (var x = selected.PieceRange.Start.Value;x < selected.PieceRange.End.Value;x++) {
                Pieces[x] = PieceState.Highlighted;
            }
        }

        Dispatcher.UIThread.Invoke(() => {
            Pieces = [.. Pieces]; // Force UI update
        });
    }

    public bool CanExecuteCancelFx() => CanExecuteCancel;
    [RelayCommand(CanExecute = nameof(CanExecuteCancelFx))]
    public void Cancel()
    {
        Cancellation.Cancel();
    }
}
