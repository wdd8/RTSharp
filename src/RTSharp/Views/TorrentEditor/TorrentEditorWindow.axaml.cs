using Avalonia.Platform.Storage;

using BencodeNET.Objects;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentEditor;

using System.Threading.Tasks;

namespace RTSharp.Views.TorrentEditor;

public partial class TorrentEditorWindow : VmWindow<TorrentEditorWindowViewModel>
{
    public TorrentEditorWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.SelectFileDestDialog = SelectFileDestDialogAsync;
            vm!.ShowBencodeEditor = ShowBencodeEditorAsync;
            vm!.CloseWindow = Close;
        });
    }

    private async Task<string?> SelectFileDestDialogAsync(string SuggestedName)
    {
        var res = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
            DefaultExtension = ".torrent",
            FileTypeChoices = [
                new(".torrent files") {
                    Patterns = [ "*.torrent" ]
                }
            ],
            Title = "Save .torrent as...",
            ShowOverwritePrompt = true,
            SuggestedFileName = SuggestedName
        });

        return res?.TryGetLocalPath();
    }

    private async Task<BDictionary?> ShowBencodeEditorAsync(BDictionary Root)
    {
        var wnd = new BencodeEditorWindow {
            ViewModel = new BencodeEditorWindowViewModel(Root)
        };

        return await wnd.ShowDialog<BDictionary?>(this);
    }
}
