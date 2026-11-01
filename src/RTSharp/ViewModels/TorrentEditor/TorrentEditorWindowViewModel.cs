using Avalonia.Controls;
using Avalonia.Controls.DataGridHierarchical;

using BencodeNET.Objects;
using BencodeNET.Parsing;
using BencodeNET.Torrents;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;
using RTSharp.ViewModels.TorrentListing;

using Serilog;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

using Torrent = RTSharp.Models.Torrent;

namespace RTSharp.ViewModels.TorrentEditor;

public partial class TorrentEditorWindowViewModel : ObservableObject
{
    private static readonly HashSet<string> EditableFields = [
        nameof(Name), nameof(Comment), nameof(CreatedBy), nameof(CreationDate), nameof(Encoding),
        nameof(Source), nameof(IsPrivate), nameof(TrackersText), nameof(WebSeedsText), nameof(HttpSeedsText), nameof(NodesText)
    ];

    private static readonly HashSet<string> KnownRootKeys = [
        "announce", "announce-list", "url-list", "httpseeds", "nodes", "comment", "comment.utf-8",
        "created by", "creation date", "encoding", "info", "piece layers"
    ];

    private static readonly HashSet<string> KnownInfoKeys = [
        "name", "name.utf-8", "piece length", "pieces", "length", "files", "file tree", "private", "source", "meta version"
    ];

    public Torrent Torrent { get; }

    private readonly byte[] OriginalBytes;
    private readonly byte[] OriginialHash;
    private readonly string OriginalName;
    private readonly BencodeUtils.TorrentFile[] OriginalFiles;
    private BDictionary Root;
    private bool Loading;

    public Func<string, Task<string?>> SelectFileDestDialog { get; set; } = null!; // view set

    public Func<BDictionary, Task<BDictionary?>> ShowBencodeEditor { get; set; } = null!; // view set

    public Action CloseWindow { get; set; } = null!; // view set

    public ObservableCollection<DataGridColumnDefinition> ColumnDefinitions { get; } = [];

    public HierarchicalModel<Models.File> FilesModel { get; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Comment { get; set; } = "";

    [ObservableProperty]
    public partial string CreatedBy { get; set; } = "";

    /// <summary>UTC</summary>
    [ObservableProperty]
    public partial DateTime? CreationDate { get; set; }

    [ObservableProperty]
    public partial string Encoding { get; set; } = "";

    [ObservableProperty]
    public partial string Source { get; set; } = "";

    [ObservableProperty]
    public partial bool IsPrivate { get; set; }

    /// <summary>One URL per line, blank line between tiers</summary>
    [ObservableProperty]
    public partial string TrackersText { get; set; } = "";

    [ObservableProperty]
    public partial string WebSeedsText { get; set; } = "";

    [ObservableProperty]
    public partial string HttpSeedsText { get; set; } = "";

    /// <summary>One IP:port per line</summary>
    [ObservableProperty]
    public partial string NodesText { get; set; } = "";

    [ObservableProperty]
    public partial string? HashWarning { get; set; }

    [ObservableProperty]
    public partial string? FilesWarning { get; set; }

    [ObservableProperty]
    public partial string InfoVersion { get; set; } = "";

    [ObservableProperty]
    public partial string InfoPieceLength { get; set; } = "";

    [ObservableProperty]
    public partial string InfoPieceCount { get; set; } = "";

    [ObservableProperty]
    public partial string InfoTotalSize { get; set; } = "";

    [ObservableProperty]
    public partial string InfoFileCount { get; set; } = "";

    [ObservableProperty]
    public partial string InfoMetadataSize { get; set; } = "";

    [ObservableProperty]
    public partial string InfoOriginalHash { get; set; } = "";

    [ObservableProperty]
    public partial string InfoCurrentV1Hash { get; set; } = "";

    [ObservableProperty]
    public partial string InfoCurrentV2Hash { get; set; } = "";

    [ObservableProperty]
    public partial string InfoOtherKeys { get; set; } = "";

    public TorrentEditorWindowViewModel(Torrent Torrent, byte[] DotTorrent)
    {
        this.Torrent = Torrent;
        OriginalBytes = DotTorrent;

        using (var ms = new MemoryStream(DotTorrent))
            Root = new BencodeParser().Parse<BDictionary>(ms);

        var info = Root.Get<BDictionary>(TorrentFields.Info) ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");
        var (v1, v2) = BencodeUtils.GetInfoHashes(Root);
        OriginialHash = (v2 ?? v1)!;
        OriginalName = BencodeUtils.GetName(info);
        OriginalFiles = BencodeUtils.GetFiles(info).Files;

        FilesModel = new HierarchicalModel<Models.File>(new HierarchicalOptions<Models.File> {
            ChildrenSelector = x => x.Children,
            IsLeafSelector = x => !x.IsDirectory,
            AutoExpandRoot = true,
            MaxAutoExpandDepth = 2,
            VirtualizeChildren = true
        });

        ColumnDefinitions.AddRange([
            new DataGridHierarchicalColumnDefinition {
                Header = "Name",
                Binding = ColumnDefinitionBindingFactory.CreateBinding<HierarchicalNode, Models.File>("Item", node => (Models.File)node.Item),
                CellTemplateKey = "FileNameTemplate",
                ColumnKey = nameof(Models.File.Name),
                SortMemberPath = nameof(Models.File.Name)
            },
            new DataGridTemplateColumnDefinition {
                Header = nameof(Models.File.Size),
                ValueAccessor = new DataGridColumnValueAccessor<Models.File, Models.File>(x => x),
                CellTemplateKey = "FileSizeTemplate",
                ColumnKey = nameof(Models.File.Size),
                SortMemberPath = nameof(Models.File.Size)
            }
        ]);

        LoadFields();
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (!Loading && e.PropertyName != null && EditableFields.Contains(e.PropertyName))
            RefreshInfo();
    }

    private static async Task<ButtonResult> ShowMessage(string Message, Icon Icon = Icon.Error, ButtonEnum Buttons = ButtonEnum.Ok)
    {
        var msgBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
            ButtonDefinitions = Buttons,
            ContentTitle = "RT# - Edit torrent",
            ContentMessage = Message,
            Icon = Icon
        });
        return await msgBox.ShowWindowAsync();
    }

    private void LoadFields()
    {
        var info = Root.Get<BDictionary>(TorrentFields.Info) ?? throw new InvalidDataException("Torrent has no \"info\" dictionary");

        Loading = true;
        try {
            Name = info.Get<BString>("name")?.ToString() ?? info.Get<BString>("name.utf-8")?.ToString() ?? "";
            Comment = BencodeUtils.GetComment(Root) ?? "";
            CreatedBy = BencodeUtils.GetCreatedBy(Root) ?? "";
            CreationDate = BencodeUtils.GetCreationDate(Root);
            Encoding = BencodeUtils.GetEncoding(Root) ?? "";
            Source = BencodeUtils.GetSource(info) ?? "";
            IsPrivate = BencodeUtils.IsPrivate(info);
            TrackersText = String.Join("\n\n", BencodeUtils.GetAnnounceTiers(Root).Select(t => String.Join('\n', t)));
            WebSeedsText = String.Join('\n', BencodeUtils.GetWebSeeds(Root));
            HttpSeedsText = String.Join('\n', BencodeUtils.GetHttpSeeds(Root));
            NodesText = String.Join('\n', BencodeUtils.GetNodes(Root).Select(n => n.ToString()));
        } finally {
            Loading = false;
        }

        RefreshInfo();
    }

    /// <summary>Writes the form into <see cref="Root" />; info keys are only touched when changed, as each rewrite alters the info hash</summary>
    private void ApplyFields()
    {
        var info = Root.Get<BDictionary>(TorrentFields.Info)!;

        if (Name != "" && Name != (info.Get<BString>("name")?.ToString() ?? info.Get<BString>("name.utf-8")?.ToString()))
            BencodeUtils.SetName(info, Name);

        if (Source != (BencodeUtils.GetSource(info) ?? ""))
            BencodeUtils.SetSource(info, Source);

        if (IsPrivate != BencodeUtils.IsPrivate(info))
            BencodeUtils.SetPrivate(info, IsPrivate);

        BencodeUtils.SetComment(Root, Comment);
        BencodeUtils.SetCreatedBy(Root, CreatedBy);
        BencodeUtils.SetCreationDate(Root, CreationDate);
        BencodeUtils.SetEncoding(Root, Encoding);

        BencodeUtils.SetAnnounceTiers(Root, [.. TrackersText
            .Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tier => tier.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))]);
        BencodeUtils.SetWebSeeds(Root, WebSeedsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        BencodeUtils.SetHttpSeeds(Root, HttpSeedsText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        // Nodes with a hostname are not editable here and survive as long as the user doesn't change the list
        var nodes = new List<IPEndPoint>();
        foreach (var line in NodesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
            if (IPEndPoint.TryParse(line, out var node))
                nodes.Add(node);
        }
        if (!nodes.SequenceEqual(BencodeUtils.GetNodes(Root)))
            BencodeUtils.SetNodes(Root, nodes);
    }

    private void RefreshInfo()
    {
        ApplyFields();

        var info = Root.Get<BDictionary>(TorrentFields.Info)!;
        var (v1, v2) = BencodeUtils.GetInfoHashes(Root);
        var currentHash = (v2 ?? v1)!;

        var hasV1 = info.ContainsKey("pieces");
        var hasV2 = info.ContainsKey("file tree");
        InfoVersion = hasV1 && hasV2 ? "Hybrid (v1 + v2)" : hasV2 ? "v2" : "v1";
        InfoOriginalHash = Convert.ToHexString(Torrent.Hash);
        InfoCurrentV1Hash = v1 == null ? "-" : Convert.ToHexString(v1);
        InfoCurrentV2Hash = v2 == null ? "-" : Convert.ToHexString(v2);
        InfoMetadataSize = Shared.Utils.Converters.GetSIDataSize((ulong)info.GetSizeInBytes());

        InfoOtherKeys = String.Join("; ", new[] {
            ("root", Root.Keys.Select(k => k.ToString()).Where(k => !KnownRootKeys.Contains(k))),
            ("info", info.Keys.Select(k => k.ToString()).Where(k => !KnownInfoKeys.Contains(k)))
        }.Where(x => x.Item2.Any()).Select(x => x.Item1 + ": " + String.Join(", ", x.Item2)));
        if (InfoOtherKeys == "")
            InfoOtherKeys = "-";

        try {
            var name = BencodeUtils.GetName(info);
            var (files, multiFile) = BencodeUtils.GetFiles(info);
            var pieceLength = BencodeUtils.GetPieceLength(info);
            var totalSize = files.Sum(f => f.FileSize);

            InfoPieceLength = Shared.Utils.Converters.GetSIDataSize((ulong)pieceLength);
            InfoPieceCount = (hasV1 ? BencodeUtils.GetPieces(info)!.Length / InfoHash.V1Length : (totalSize + pieceLength - 1) / pieceLength).ToString();
            InfoTotalSize = Shared.Utils.Converters.GetSIDataSize((ulong)totalSize);
            InfoFileCount = files.Length.ToString();

            FilesModel.SetRoot(Models.File.FromBencode(name, multiFile, files).Root);

            FilesWarning = name == OriginalName
                && files.Length == OriginalFiles.Length
                && files.Zip(OriginalFiles).All(x => x.First.FileSize == x.Second.FileSize && x.First.PathComponents.SequenceEqual(x.Second.PathComponents))
                ? null
                : "Name or files changed: the content no longer matches the data on disk, so fast resume will fail and the torrent needs a recheck.";
        } catch (InvalidDataException) {
            InfoPieceLength = InfoPieceCount = InfoTotalSize = InfoFileCount = "?";
            FilesModel.ClearAll();
            FilesWarning = null;
        }

        HashWarning = currentHash.SequenceEqual(Torrent.Hash)
            ? null
            : $"Info hash changes ({Convert.ToHexString(Torrent.Hash)} -> {Convert.ToHexString(currentHash)}): the edited torrent is a different torrent in the swarm."
              + (currentHash.SequenceEqual(OriginialHash) ? " The original .torrent is not canonically encoded, so any save changes its hash." : "");
    }

    /// <summary>Applies the form and validates the result, showing the problem if there is one</summary>
    private async Task<bool> ApplyAndValidate()
    {
        ApplyFields();

        string? error = null;
        var info = Root.Get<BDictionary>(TorrentFields.Info);

        if (info == null)
            error = "Torrent has no \"info\" dictionary";
        else if (Name == "")
            error = "Name cannot be empty";
        else if (BencodeUtils.GetAnnounceTiers(Root).SelectMany(x => x).Any(x => !Uri.TryCreate(x, UriKind.Absolute, out _)))
            error = "Invalid tracker URL";
        else if (BencodeUtils.GetWebSeeds(Root).Any(x => !Uri.TryCreate(x, UriKind.Absolute, out _)))
            error = "Invalid web seed URL";
        else if (BencodeUtils.GetHttpSeeds(Root).Any(x => !Uri.TryCreate(x, UriKind.Absolute, out _)))
            error = "Invalid HTTP seed URL";
        else if (NodesText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length != BencodeUtils.GetNodes(Root).Length)
            error = "Nodes must be in the IP:port format";
        else {
            try {
                BencodeUtils.GetPieceLength(info);
                BencodeUtils.GetFiles(info);
            } catch (InvalidDataException ex) {
                error = ex.Message;
            }
        }

        if (error != null)
            await ShowMessage(error);

        return error == null;
    }

    [RelayCommand]
    public async Task AdvancedClick()
    {
        ApplyFields();

        var edited = await ShowBencodeEditor(Root);
        if (edited == null)
            return;

        Root = edited;
        LoadFields();
    }

    [RelayCommand]
    public void RevertClick()
    {
        using var ms = new MemoryStream(OriginalBytes);
        Root = new BencodeParser().Parse<BDictionary>(ms);
        LoadFields();
    }

    [RelayCommand]
    public void SetCreationDateNowClick()
    {
        CreationDate = DateTime.UtcNow;
    }

    [RelayCommand]
    public async Task SaveAsClick()
    {
        if (!await ApplyAndValidate())
            return;

        var path = await SelectFileDestDialog(Name + ".torrent");
        if (path == null)
            return;

        try {
            await System.IO.File.WriteAllBytesAsync(path, Root.EncodeAsBytes());
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to save .torrent");
            await ShowMessage("Failed to save .torrent:\n" + ex.Message);
        }
    }

    private bool CanApply()
    {
        var caps = Torrent.DataOwner.Instance.Capabilities;
        return caps.RemoveTorrent && caps.AddTorrent && caps.FastResumeOnAdd;
    }
    [RelayCommand(CanExecute = nameof(CanApply))]
    public async Task ApplyClick()
    {
        if (!await ApplyAndValidate())
            return;

        var dp = Torrent.DataOwner;
        var queue = Core.ActionQueue.GetActionQueueEntry(dp.Host);
        if (queue == null)
            return;

        var (v1, v2) = BencodeUtils.GetInfoHashes(Root);
        var newHash = (v2 ?? v1)!;

        var message = $"Replace \"{Torrent.Name}\" on {dp.Config.Plugin.Name}?\n\n" +
            $"The torrent is removed (its data is kept) and re-added from {Torrent.RemotePath} with fast resume.\n\n" +
            $"This resets its statistics (uploaded {Shared.Utils.Converters.GetSIDataSize(Torrent.Uploaded)}, " +
            $"downloaded {Shared.Utils.Converters.GetSIDataSize(Torrent.Downloaded)}, ratio {Torrent.Ratio:0.00}, added on {Torrent.AddedOnDate.ToLocalTime()}) " +
            "and any per-torrent settings kept by the data provider other than labels, such as file priorities and speed limits.";
        if (HashWarning != null)
            message += "\n\n" + HashWarning;
        if (FilesWarning != null)
            message += "\n\n" + FilesWarning;

        if (await ShowMessage(message, Icon.Warning, ButtonEnum.YesNo) != ButtonResult.Yes)
            return;

        var oldHash = Torrent.Hash;
        var labels = Torrent.Labels;
        var filename = Convert.ToHexString(oldHash) + ".torrent";
        var options = new AddTorrentsOptions(null, Torrent.RemotePath, true);
        var edited = Root.EncodeAsBytes();
        var instance = dp.Instance;
        var startAfterAdd = Torrent.InternalState is TORRENT_STATE.DOWNLOADING or TORRENT_STATE.SEEDING
            && instance.Capabilities.ForceStartTorrentOnAdd == null;

        static void throwIfFailed(TorrentStatuses Result)
        {
            if (Result.Any(x => x.Exceptions.Any(e => e != null)))
                throw new Exception("Operation failed:\n" + String.Join('\n', Result.Select(x => Convert.ToHexString(x.Hash) + ": " + String.Join(", ", x.Exceptions.Where(e => e != null).Select(e => e.Message)))));
        }

        Exception? failure = null;
        var action = ActionQueueAction.New("Replace torrent", async (action) => {
            try {
                action.ChangeProgress(0f, "Removing torrent");
                throwIfFailed(await instance.RemoveTorrents([ oldHash ]));

                action.ChangeProgress(33f, "Adding edited torrent");
                try {
                    throwIfFailed(await instance.AddTorrents([ (edited, null, filename, options) ]));
                } catch (Exception ex) {
                    Log.Logger.Error(ex, "Failed to add edited torrent, restoring the original");

                    try {
                        throwIfFailed(await instance.AddTorrents([ (OriginalBytes, null, filename, options) ]));
                        if (startAfterAdd)
                            await instance.StartTorrents([ oldHash ]);
                    } catch (Exception restoreEx) {
                        Log.Logger.Error(restoreEx, "Failed to restore the original torrent");
                    }

                    throw;
                }

                action.ChangeProgress(66f, "Restoring labels and state");
                if (labels.Length != 0 && instance.Capabilities.AddLabel)
                    throwIfFailed(await instance.SetLabels([ (newHash, labels) ]));

                if (startAfterAdd)
                    throwIfFailed(await instance.StartTorrents([ newHash ]));
            } catch (Exception ex) {
                failure = ex;
                throw;
            }
        });

        IsBusy = true;
        try {
            await queue.Queue.RunAction(action);
        } catch (Exception ex) {
            failure ??= ex;
        } finally {
            IsBusy = false;
        }

        if (failure != null) {
            Log.Logger.Error(failure, "Failed to replace torrent");
            await ShowMessage("Failed to replace torrent:\n" + failure.Message);
            return;
        }

        CloseWindow();
    }

    [RelayCommand]
    private void CancelClick()
    {
        CloseWindow();
    }
}
