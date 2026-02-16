using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.Tools;

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;

namespace RTSharp.Views.Tools;

public partial class TorrentCreatorWindow : VmWindow<TorrentCreatorWindowViewModel>
{
    public TorrentCreatorWindow()
    {
        InitializeComponent();

        DragDropPanel.AddHandler(DragDrop.DropEvent, EvDragDrop);

        BindViewModelActions(vm => {
            vm!.StrPieceLength = (ComboBoxItem)CmbPieceLength.Items[0];
            vm!.SelectFileDialog = SelectFileDialogAsync;
            vm!.SelectFolderDialog = SelectFolderDialogAsync;
            vm!.SelectFileDestDialog = SelectFileDestDialogAsync;
            vm!.CloseWindow = CloseWindow;
        });
    }

    private async Task<string?> SelectFileDialogAsync()
    {
        var res = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
            AllowMultiple = false,
            FileTypeFilter = new FilePickerFileType[] {
                new("All files") {
                    Patterns = new List<string> {
                        "*.*"
                    }
                }
            },
            Title = "Select file..."
        });

        var path = res.Select(x => x.Path.AbsolutePath).FirstOrDefault();
        return path == default ? null : HttpUtility.UrlDecode(path);
    }

    private async Task<string?> SelectFileDestDialogAsync(string SuggestedName)
    {
        var res = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            DefaultExtension = ".torrent",
            FileTypeChoices = new FilePickerFileType[] {
                new(".torrent files") {
                    Patterns = new List<string> {
                        "*.torrent"
                    }
                }
            },
            Title = "Select file...",
            ShowOverwritePrompt = true,
            SuggestedFileName = SuggestedName
        });

        return res?.Path?.AbsolutePath == null ? null : HttpUtility.UrlDecode(res.Path.AbsolutePath);
    }

    private async Task<string?> SelectFolderDialogAsync()
    {
        var res = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            AllowMultiple = false,
            Title = "Select folder..."
        });

        var path = res.Select(x => x.Path.AbsolutePath).FirstOrDefault();
        return path == default ? null : HttpUtility.UrlDecode(path);
    }

    public async void EvDragDrop(object? sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        if (files?.Any() == false)
            return;

        try {
            await ViewModel!.DragDropCommand.ExecuteAsync(HttpUtility.UrlDecode(files!.First().Path.AbsolutePath));
        } catch { }
    }

    public void CloseWindow()
    {
        this.Close();
    }
}