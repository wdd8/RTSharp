using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;

using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.ViewModels.Tools
{
    public partial class TorrentCreatorWindowViewModel : ObservableObject
    {
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsMultiFileChecked))]
        [NotifyPropertyChangedFor(nameof(IsMultiFileEnabled))]
        [NotifyCanExecuteChangedFor(nameof(CreateTorrentClickCommand))]
        public partial string? SourcePath { get; set; }
        public Func<Task<string?>> SelectFolderDialog { get;  set; }

        public Func<Task<string?>> SelectFileDialog { get; set; }

        public Action CloseWindow { get; set; }

        public Func<string, Task<string?>> SelectFileDestDialog { get; set; }

        private bool isMultiFileCheckedUserChoice;
        public bool IsMultiFileChecked {
            get {
                if (SourcePath == null)
                    return isMultiFileCheckedUserChoice;

                if (Directory.Exists(SourcePath)) {
                    return true;
                }

                return isMultiFileCheckedUserChoice;
            }
            set {
                this.SetProperty(ref isMultiFileCheckedUserChoice, value);
            }
        }

        [ObservableProperty]
        public partial uint NumberOfPieces { get; set; } = 0;

        public bool IsMultiFileEnabled {
            get {
                if (SourcePath == null)
                    return true;

                if (Directory.Exists(SourcePath)) {
                    return false;
                }

                return true;
            }
        }

        [ObservableProperty]
        public partial ComboBoxItem StrPieceLength { get; set; }

        public int? PieceLength => StrPieceLength.Content switch {
            "Auto" => null,
            "16 KiB" => 16 * 1024,
            "32 KiB" => 32 * 1024,
            "64 KiB" => 64 * 1024,
            "128 KiB" => 128 * 1024,
            "256 KiB" => 256 * 1024,
            "512 KiB" => 512 * 1024,
            "1 MiB" => 1 * 1024 * 1024,
            "2 MiB" => 2 * 1024 * 1024,
            "4 MiB" => 4 * 1024 * 1024,
            "8 MiB" => 8 * 1024 * 1024,
            "16 MiB" => 16 * 1024 * 1024,
            "32 MiB" => 32 * 1024 * 1024,
            _ => throw new ArgumentOutOfRangeException()
        };

        [ObservableProperty]
        public partial bool PrivateTorrent { get; set; }

        [ObservableProperty]
        public partial string TrackerUrls { get; set; }

        [ObservableProperty]
        public partial string WebSeedUrls { get; set; }

        [ObservableProperty]
        public partial string Comments { get; set; }

        [ObservableProperty]
        public partial string Source { get; set; }

        [ObservableProperty]
        public partial bool Entropy { get; set; }

        [ObservableProperty]
        public partial bool EmitCreationDate { get; set; } = true;

        public partial class ProgressInfo : ObservableObject
        {
            [ObservableProperty]
            public partial bool Shown { get; set; }

            [ObservableProperty]
            public partial float HashProgress { get; set; }

            [ObservableProperty]
            public partial string CurrentFile { get; set; }

            [ObservableProperty]
            public partial float FileProgress { get; set; }

            [ObservableProperty]
            public partial float FileBuffer { get; set; }

            [ObservableProperty]
            public partial string HashExcerpt { get; set; }

            public CancellationTokenSource Cts;
        }

        public ProgressInfo ProgressInfoInstance { get; } = new ProgressInfo();

        TorrentCreator Creator;

        public TorrentCreatorWindowViewModel()
        {
            Creator = new TorrentCreator();
        }

        [RelayCommand]
        public async Task SelectFileClick()
        {
            var res = await SelectFileDialog();
            if (res != null) {
                SourcePath = res;
            }
        }

        [RelayCommand]
        public async Task SelectFolderClick()
        {
            var res = await SelectFolderDialog();
            if (res != null) {
                SourcePath = res;
            }
        }

        [RelayCommand]
        public Task DragDrop(string Input)
        {
            SourcePath = Input;

            return Task.CompletedTask;
        }

        private bool CanExecuteCreateTorrentClick() => SourcePath != null;
        [RelayCommand(CanExecute = nameof(CanExecuteCreateTorrentClick))]
        public async Task CreateTorrentClick()
        {
            if (!File.Exists(SourcePath) && !Directory.Exists(SourcePath)) {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.Ok,
                    ContentTitle = "RT# - Torrent Creator",
                    ContentMessage = "Invalid source path",
                    Icon = MsBox.Avalonia.Enums.Icon.Error
                });
                await msgBox.ShowWindowAsync();
                return;
            }

            var res = await SelectFileDestDialog(Path.GetFileName(SourcePath));

            if (res == null)
                return;

            var trackerUrls = TrackerUrls?.Split(Environment.NewLine);
            var valid = trackerUrls == null || trackerUrls.All(x => Uri.TryCreate(x, UriKind.Absolute, out var _));

            if (!valid) {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.Ok,
                    ContentTitle = "RT# - Torrent Creator",
                    ContentMessage = "Invalid tracker URL",
                    Icon = MsBox.Avalonia.Enums.Icon.Error
                });
                await msgBox.ShowWindowAsync();
                return;
            }

            var webSeedUrls = WebSeedUrls?.Split(Environment.NewLine);
            valid = webSeedUrls == null || webSeedUrls.All(x => Uri.TryCreate(x, UriKind.Absolute, out var _));

            if (!valid) {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(new MsBox.Avalonia.Dto.MessageBoxStandardParams {
                    ButtonDefinitions = MsBox.Avalonia.Enums.ButtonEnum.Ok,
                    ContentTitle = "RT# - Torrent Creator",
                    ContentMessage = "Invalid web seed URL",
                    Icon = MsBox.Avalonia.Enums.Icon.Error
                });
                await msgBox.ShowWindowAsync();
                return;
            }

            var progress = ((float HashProgress, string CurrentFile, float FileProgress, float FileBuffer, string HashExcerpt) progress) => {
                ProgressInfoInstance.HashProgress = progress.HashProgress;
                ProgressInfoInstance.CurrentFile = progress.CurrentFile;
                ProgressInfoInstance.FileProgress = progress.FileProgress;
                ProgressInfoInstance.FileBuffer = progress.FileBuffer;
                ProgressInfoInstance.HashExcerpt = progress.HashExcerpt;
            };

            ProgressInfoInstance.Cts = new();

            ProgressInfoInstance.Shown = true;
            try {
                var file = await Creator.Create(
                    Path: SourcePath,
                    Trackers: trackerUrls,
                    WebSeeds: webSeedUrls,
                    Comments: Comments,
                    Source: Source,
                    Private: true,
                    Entropy: Entropy,
                    EmitCreationDate: EmitCreationDate,
                    PieceLength: PieceLength,
                    Parallel: true,
                    Progress: progress,
                    cancellationToken: ProgressInfoInstance.Cts.Token);
                await File.WriteAllBytesAsync(res, file);
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Failed to create torrent file");
            }
            ProgressInfoInstance.Shown = false;
        }

        [RelayCommand]
        public void CancelClick()
        {
            CloseWindow();
        }

        [RelayCommand]
        public async Task CalculateNumberOfPiecesClick()
        {
            if (SourcePath == null)
                return;

            var dataInfo = await Creator.GetDataInfo(SourcePath);

            int pieceLength;
            if (PieceLength == null)
                pieceLength = Creator.CalculatePieceLength(dataInfo.TotalSize);
            else
                pieceLength = PieceLength.Value;

            NumberOfPieces = Creator.CalculateNumberOfPieces(dataInfo.TotalSize, pieceLength);
        }

        [RelayCommand]
        public void CancelHashingClick()
        {
            ProgressInfoInstance.Cts.Cancel();
        }
    }
}
