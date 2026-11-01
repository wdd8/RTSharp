using Avalonia.Platform.Storage;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentEditor;

using System.Linq;

namespace RTSharp.Views.TorrentEditor;

public partial class BytesEditorWindow : VmWindow<BytesEditorWindowViewModel>
{
    public BytesEditorWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.OpenFileDialog = async () => (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                AllowMultiple = false,
                Title = "Import bytes from file..."
            })).FirstOrDefault()?.TryGetLocalPath();
            vm!.SaveFileDialog = async () => (await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                ShowOverwritePrompt = true,
                Title = "Export bytes to file..."
            }))?.TryGetLocalPath();
            vm!.CloseWindow = Close;
        });
    }
}
