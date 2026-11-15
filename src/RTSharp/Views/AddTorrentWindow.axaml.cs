using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Controls.Views;
using RTSharp.ViewModels;

namespace RTSharp.Views
{
    public partial class AddTorrentWindow : VmWindow<AddTorrentViewModel>
    {
        public AddTorrentWindow()
        {
            InitializeComponent();

            BindViewModelActions(vm => {
                vm!.CancelWindow = CancelWindow;
                vm!.OpenLocalFileDialog = OpenLocalFileDialogAsync;
                vm!.SelectRemoteDirectoryDialog = SelectRemoteDirectoryDialogAsync;
                vm!.PreviewClipboard = PreviewClipboardAsync;
                vm!.GetClipboard = GetClipboardAsync;
                vm!.DuplicationTargetSelector.SelectRemoteDirectoryDialog = SelectDuplicationTargetRemoteDirectoryDialogAsync;
            });
        }

        protected override void OnOpened(EventArgs e)
        {
            SizeToContent = SizeToContent.Manual;
            base.OnOpened(e);
        }

        private Task<string?> SelectDuplicationTargetRemoteDirectoryDialogAsync(string? Input) =>
            DuplicationTargetSelectorControl.DefaultSelectRemoteDirectoryDialogImpl(this, ViewModel!.DuplicationTargetSelector.SelectedProvider, Input);

        private async Task<string?> SelectRemoteDirectoryDialogAsync(string? Input)
        {
            var dialog = new DirectorySelectorWindow()
            {
                ViewModel = new DirectorySelectorWindowViewModel(ViewModel!.SelectedProvider!.Host.AttachedDaemonService)
                {
                    WindowTitle = $"RT# - Select Directory ({ViewModel!.SelectedProvider!.Host.Config.Plugin.Name})"
                }
            };

            await dialog.ViewModel.SetCurrentFolder(Input);
            var result = await dialog.ShowDialog<string?>(this);
            return result;
        }

        private void CancelWindow()
        {
            this.Close();
        }

        private async Task<IEnumerable<IStorageFile>> OpenLocalFileDialogAsync(string? Input)
        {
            var folder = Input == null ? null : await StorageProvider.TryGetFolderFromPathAsync(Input);

            var res = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                AllowMultiple = true,
                FileTypeFilter = new List<FilePickerFileType>() {
                    new("Torrent files") {
                        Patterns = new List<string> {
                            "*.torrent"
                        }
                    }
                },
                Title = "Select Torrent Files",
                SuggestedStartLocation = folder
            });

            return res;
        }

        private async Task PreviewClipboardAsync()
        {
            var clipboardObj = this.Clipboard;
            if (clipboardObj == null)
                return;

            var clipboard = await clipboardObj.TryGetTextAsync();
            var textPreview = new TextPreviewWindow() {
                DataContext = clipboard,
                Title = "RT# - Clipboard Preview"
            };

            textPreview.Show(this);
        }

        public async Task<string?> GetClipboardAsync()
        {
            var clipboardObj = this.Clipboard;
            if (clipboardObj == null) {
                return null;
            }

            var clipboard = await clipboardObj.TryGetTextAsync();
            return clipboard;
        }
    }
}
