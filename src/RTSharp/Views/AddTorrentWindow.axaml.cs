using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

using RTSharp.Plugin;
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
            });
        }

        private async Task<string?> SelectRemoteDirectoryDialogAsync(string? Input)
        {
            var dialog = new DirectorySelectorWindow()
            {
                ViewModel = new DirectorySelectorWindowViewModel(ViewModel!.SelectedProvider!)
                {
                    WindowTitle = $"RT# - Select directory ({ViewModel!.SelectedProvider.PluginInstance.PluginInstanceConfig.Name})"
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
                Title = "Select .torrent files...",
                SuggestedStartLocation = folder
            });

            return res;
        }

        public async void EvDropDownClosed(object sender, EventArgs e)
        {
            await this.ViewModel!.ProviderChanged((RTSharpDataProvider)((ComboBox)sender).SelectedItem);
        }

        private async Task PreviewClipboardAsync()
        {
            var clipboardObj = this.Clipboard;
            if (clipboardObj == null)
                return;

            var clipboard = await clipboardObj.GetTextAsync();
            var textPreview = new TextPreviewWindow() {
                DataContext = clipboard,
                Title = "Clipboard preview"
            };

            textPreview.Show();
        }

        public async Task<string?> GetClipboardAsync()
        {
            var clipboardObj = this.Clipboard;
            if (clipboardObj == null) {
                return null;
            }

            var clipboard = await clipboardObj.GetTextAsync();
            return clipboard;
        }
    }
}
