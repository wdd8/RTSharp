using Avalonia.Media.Imaging;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.Configuration;

using MsBox.Avalonia;
using MsBox.Avalonia.Models;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Controls;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.AutoIntegrify.Plugin.ViewModels;

public partial class MainWindowViewModel(IPluginHost Host, MainWindow Window, Torrent Torrent) : ObservableObject, IContextPopulatedNotifyable
{
    public enum RESOLVE_STATE
    {
        PENDING,
        FILE_TOO_SMALL,
        UNRESOLVED,
        RESOLVED,
        RESOLVED_HEURISTICS
    }

    private Bitmap WaitIcon;

    public record TorrentFile(string FullPath, long FileSize);

    public partial class FileInfo : ObservableObject
    {
        public TorrentFile File { get; init; }

        public (ulong Start, ulong End, byte[] Hash)[] PieceHashes { get; init; }

        public Range PieceRange { get; init; }

        [ObservableProperty]
        public string? resolvedPath;

        [ObservableProperty]
        public Bitmap? statusIcon;

        public RESOLVE_STATE Resolved {
            get {
                return field;
            }
            set {
                field = value;
                StatusIcon = field switch {
                    RESOLVE_STATE.PENDING => BuiltInIcon.Get(BuiltInIcons.VISTA_WAIT),
                    RESOLVE_STATE.FILE_TOO_SMALL => BuiltInIcon.Get(BuiltInIcons.VISTA_BLOCK),
                    RESOLVE_STATE.UNRESOLVED => BuiltInIcon.Get(BuiltInIcons.SERIOUS_QUESTION),
                    RESOLVE_STATE.RESOLVED => BuiltInIcon.Get(BuiltInIcons.VISTA_OK),
                    RESOLVE_STATE.RESOLVED_HEURISTICS => BuiltInIcon.Get(BuiltInIcons.VISTA_OK_EXCLAMATION),
                    _ => null
                };
            }
        }
    }

    [ObservableProperty]
    public ObservableCollection<FileInfo> files = new();

    [ObservableProperty]
    public FileInfo selectedFile;

    [ObservableProperty]
    List<PieceState> pieces;

    [ObservableProperty]
    public string progressText = "Loading...";

    [ObservableProperty]
    public bool progressDialogShown;

    [ObservableProperty]
    public string statusText = "There are files still unresolved";

    [ObservableProperty]
    public string actionButtonText = "Perform pending hardlinks";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelCommand))]
    public bool canExecuteCancel = true;

    private CancellationTokenSource Cancellation = new();

    private bool IsMultiFile;

    Dictionary<string, string> PendingLinks = new();

    List<string> AdditionalPaths;
    int RandomPiecesToCheck;
    int SearchRecursionLimit;
    bool Reflink;
    bool Hardlink;

    public void OnContextPopulated()
    {
        AdditionalPaths = Host.PluginConfig.GetSection("SearchPaths").Get<List<string>>() ?? [];
        RandomPiecesToCheck = Host.PluginConfig.GetSection("RandomPiecesToCheck").Get<int?>() ?? 5;
        SearchRecursionLimit = Host.PluginConfig.GetSection("SearchRecursionLimit").Get<int?>() ?? 3;
        var linkOptions = Host.PluginConfig.GetSection("LinkOptions").Get<LinkOptions>();
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
                        var shouldBeAtPath = IsMultiFile ? string.Join('/', Torrent.RemotePath, Torrent.Name, file.File.FullPath) : string.Join('/', Torrent.RemotePath, file.File.FullPath);

                        if (shouldBeAtPath != file.ResolvedPath) {
                            PendingLinks.Add(file.ResolvedPath!, shouldBeAtPath);
                        }
                        break;
                    }
                }
            }

            if (PendingLinks.Count == 0) {
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
            ProgressText = $"Searching for {File.File.FullPath}{(WideSearch ? " (wide search)" : "")}...";
        });

        var remotePathParts = Torrent.RemotePath.Split('/');

        var script = await Torrent.DataOwner.Host.AttachedDaemonService!.RunCustomScript(
"""
#pragma usings
#pragma lib "Microsoft.Extensions.Logging.Abstractions"

using System.Text;
using System.Linq;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

public class Main(ILogger<Main> Logger) : IScript
{
    public async Task Execute(Dictionary<string, string> Variables, IScriptSession Session, CancellationToken CancellationToken)
    {
        Session.Progress.State = TASK_STATE.RUNNING;
        Session.Progress.Text = "Searching...";

        var addPaths = Variables["AdditionalPaths"].Split('\x00');
        var current = Variables["CurrentPath"];
        var filePath = Variables["FilePath"];
        var size = Int64.Parse(Variables["Size"]);
        var isMultiFile = Variables["IsMultiFile"] == "1";
        var wideSearch = Variables["WideSearch"] == "1";
        var recurseLimit = Int32.Parse(Variables["RecurseLimit"]);

        // technically not torrent name because it would lack extension on single file torrents but it's more convenient this way
        var torrentName = isMultiFile ? current.Split('/').Last() : Path.GetFileNameWithoutExtension(filePath);

        var outp = new StringBuilder();
        void append(string inp)
        {
            outp.Append(inp);
            outp.Append('\x00');
        }

        bool matchFolder(string path, int recursed = 0) {
            if (recursed >= recurseLimit)
                return false;

            try {
                var currentDir = new DirectoryInfo(path);

                Session.Progress.Text = path;

                foreach (var file in currentDir.EnumerateFiles()) {
                    if (file.FullName.EndsWith(filePath)) {
                        append(file.FullName);
                        if (size == file.Length) {
                            // Strong match, signal outside to bail
                            return true;
                        }
                        continue;
                    }
                    if (size == file.Length) {
                        append(file.FullName);
                    }
                }
                foreach (var dir in currentDir.EnumerateDirectories()) {
                    if (matchFolder(dir.FullName, recursed + 1)) {
                        return true;
                    }
                }
            } catch {
                Logger.LogWarning($"AutoIntegrify: Cannot query directory {path}");
            }

            return false;
        }

        // First try exact matches in current folder
        if (matchFolder(current)) {
            goto done;
        }

        foreach (var path in addPaths) {
            try {
                var dir = new DirectoryInfo(path);
                foreach (var info in dir.EnumerateFileSystemInfos()) {
                    if (info.Attributes.HasFlag(FileAttributes.Directory)) {
                        if (isMultiFile) {
                            if (info.Name == torrentName || wideSearch) {
                                Logger.LogInformation($"Considering {info.FullName}...");
                                if (matchFolder(info.FullName)) {
                                    // Got a strong match, I don't think we will need other folders...
                                    goto done;
                                }
                            }
                        } else {
                            if (info.Name == torrentName || wideSearch) {
                                try {
                                    var thisFile = new FileInfo(info.FullName);
                                    if (thisFile.Length == size) {
                                        append(info.FullName);
                                        // Got a strong match, I don't think we will need other than this...
                                        goto done;
                                    }
                                } catch {
                                    Logger.LogWarning($"AutoIntegrify: Cannot query file {info.FullName}");
                                }
                            }
                        }
                    } else {
                        if (info.FullName.EndsWith(filePath) || wideSearch) {
                            try {
                                var thisFile = new FileInfo(info.FullName);
                                if (thisFile.Length == size) {
                                    append(info.FullName);
                                    continue;
                                }
                            } catch {
                                Logger.LogWarning($"AutoIntegrify: Cannot query file {info.FullName}");
                            }
                        }
                    }
                }
            } catch {
                Logger.LogWarning($"AutoIntegrify: Cannot query directory {path}");
            }
        }

done:;
        Session.Progress.StateData = outp.ToString().TrimEnd('\x00');
        Session.Progress.State = TASK_STATE.DONE;
    }
}
""", $"AutoIntegrify_{Guid.NewGuid()}", new Dictionary<string, string> {
            ["AdditionalPaths"] = String.Join('\x00', AdditionalPaths),
            ["FilePath"] = File.File.FullPath,
            ["Size"] = File.File.FileSize.ToString(),
            ["CurrentPath"] = IsMultiFile ? $"{Torrent.RemotePath}/{Torrent.Name}" : Torrent.RemotePath,
            ["LimitPath"] = string.Join('/', remotePathParts[..^1]),
            ["IsMultiFile"] = IsMultiFile ? "1" : "",
            ["WideSearch"] = WideSearch ? "1" : "",
            ["RecurseLimit"] = SearchRecursionLimit.ToString()
});

        Cancellation.Token.Register(() => {
            _ = Torrent?.DataOwner?.Host?.AttachedDaemonService?.QueueScriptCancellation(script);
        });

        List<string> paths = [];

        await Torrent.DataOwner.Host.AttachedDaemonService.GetScriptProgress(script, progress => {
            if (progress.State == TASK_STATE.DONE) {
                paths = [.. progress.StateData!.Split('\x00', StringSplitOptions.RemoveEmptyEntries)];
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = $"Searching for {File.File.FullPath}{(WideSearch ? " (wide search)" : "")}: Done";
                });
            } else {
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = $"Searching for {File.File.FullPath}{(WideSearch ? " (wide search)" : "")}: {progress.Text}";
                });
            }
        });

        if (paths.Count == 0) {
            Dispatcher.UIThread.Invoke(() => {
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = $"No files found for {File.File.FullPath}";
                });
                File.Resolved = RESOLVE_STATE.UNRESOLVED;
                RefreshPieces(File, SelectedFile);
            });
            return false;
        }

        if (paths.Count > 20) {
            paths = paths[..20];
        }

        Dispatcher.UIThread.Invoke(() => {
            ProgressText = $"{File.File.FullPath}: Verifying random pieces...";
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
        var startPiece = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, file.PieceHashes[0].Start, file.PieceHashes[0].End, HashAlgorithmName.SHA1);
        if (!startPiece.SequenceEqual(file.PieceHashes[0].Hash)) {
            return false;
        }

        if (file.PieceHashes.Length > 1) {
            var lastPiece = file.PieceHashes.Last();
            var hash = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, lastPiece.Start, lastPiece.End, HashAlgorithmName.SHA1);
            if (!hash.SequenceEqual(lastPiece.Hash)) {
                return false;
            }
        }

        for (var x = 0; x < Math.Min(RandomPiecesToCheck, file.PieceHashes.Length - 2);x++) {
            var piece = file.PieceHashes[Random.Shared.Next(1, file.PieceHashes.Length - 2)];
            var hash = await Torrent.DataOwner.Host.AttachedDaemonService!.HashFileBlock(resolvedPath, piece.Start, piece.End, HashAlgorithmName.SHA1);
            if (!hash.SequenceEqual(piece.Hash)) {
                return false;
            }
        }

        return true;
    }

    private async Task AssignHashesToPieces()
    {
        var dotTorrent = await Torrent.DataOwner.GetDotTorrents([ Torrent ]);
        using var mem = new System.IO.MemoryStream(dotTorrent.Single().Value);

        BencodeNET.Torrents.Torrent btorrent;
        var parser = new BencodeNET.Torrents.TorrentParser();
        try {
            btorrent = parser.Parse(mem);
        } catch {
            throw;
        }

        var blockSize = 20;

        var pieces = new List<PieceState>();

        if (btorrent.Files == null)
            IsMultiFile = false;
        else
            IsMultiFile = true;

        var files = btorrent.Files?.Select(x => new TorrentFile(
            FullPath: x.FullPath.Replace(System.IO.Path.DirectorySeparatorChar, '/'),
            FileSize: x.FileSize
        ))?.ToArray() ?? [ new TorrentFile(
            FullPath: btorrent.File.FileName,
            FileSize: btorrent.File.FileSize
        ) ];

        for (var x = 0;x < files.Length;x++) {
            var file = files[x];

            if (file.FileSize < btorrent.PieceSize) {
                Dispatcher.UIThread.Invoke(() => {
                    ProgressText = $"{file}: File too small";
                    Files.Add(new FileInfo
                    {
                        File = file,
                        PieceHashes = [],
                        Resolved = RESOLVE_STATE.FILE_TOO_SMALL,
                        PieceRange = 0..0
                    });
                });
                continue;
            }

            /*
             * Assuming:
             * 
             * Torrent containing:
             * file1 (1000 bytes)
             * file2 (3000 bytes)
             * file3 (2000 bytes)
             * file4 (405000 bytes)
             * file5 (155120 bytes)
             * 
             * Piece length: 16384 bytes
             * 
             * Current `file`: file4
             */

            /*
             * Start bytes in the whole torrent
             * 
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                         ^
             */
            var start = files[..x].Sum(x => x.FileSize);

            var unaligned = start % btorrent.PieceSize != 0 ? 1 : 0;

            /*
             * Start bytes in the whole torrent aligned to piece size, but after `baseStart`
             * 
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                         ^^
             */
            var startPieceAligned = ((start / btorrent.PieceSize) + unaligned) * btorrent.PieceSize;

            /*
             * End bytes in the whole torrent
             * 
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                                             ^
             */
            var end = start + file.FileSize;

            /*
             * End bytes in the whole torrent aligned to piece size, but before end bytes in the whole torrent
             * 
             * |file1|--file2--|-file3-|---Z---file4---Z---|--Z--file5--Z--|
             *                                            ^^
             */
            var endingBytesAligned = (end / btorrent.PieceSize) * btorrent.PieceSize;

            /*
             * Offset of starting piece of the file
             * 
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          ^
             */
            var startingPiecesBytes = (int)(startPieceAligned / btorrent.PieceSize) * blockSize;

            // Same for ending...
            var endingPiecesBytes = (int)(endingBytesAligned / btorrent.PieceSize) * blockSize;

            /*
             * Pieces that belong to the file
             * 
             * "pieces": "<hex>|pieces_for_file1|--pieces_for_file2--|-pieces_for_file3-|---Z---pieces_for_file4---Z---|--Z--pieces_for_file5--Z--|</hex>"
             *                                                                          <------------------------------>
             */
            var piecesBytesRange = btorrent.Pieces[startingPiecesBytes..endingPiecesBytes];

            // TODO: SHA-256
            Files.Add(new FileInfo {
                File = file,
                PieceHashes = [.. piecesBytesRange.Chunk(blockSize).Select((x, i) => (
                    Start: (ulong)((startPieceAligned - start) + ((i) * btorrent.PieceSize)),
                    End: (ulong)((startPieceAligned - start) + ((i + 1) * btorrent.PieceSize)),
                    Hash: x
                ))],
                PieceRange = (startingPiecesBytes/blockSize)..(endingPiecesBytes/blockSize),
                Resolved = RESOLVE_STATE.PENDING
            });
        }

        pieces.AddRange(Enumerable.Repeat(PieceState.NotDownloaded, (int)Math.Ceiling(files.Sum(x => x.FileSize) / (float)btorrent.PieceSize)));

        Dispatcher.UIThread.Invoke(() => {
            Pieces = pieces;
        });
    }

    [RelayCommand]
    public async Task ActionButton()
    {
        if (PendingLinks.Count != 0) {
            await Torrent.DataOwner.Host.AttachedDaemonService!.LinkFiles([.. PendingLinks], Reflink, Hardlink);
        } else {
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

    partial void OnSelectedFileChanging(FileInfo? oldValue, FileInfo newValue)
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
