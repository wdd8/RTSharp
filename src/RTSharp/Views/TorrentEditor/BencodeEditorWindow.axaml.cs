using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.TorrentEditor;

namespace RTSharp.Views.TorrentEditor;

public partial class BencodeEditorWindow : VmWindow<BencodeEditorWindowViewModel>
{
    public BencodeEditorWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.ShowBytesEditor = async Bytes => await new BytesEditorWindow {
                ViewModel = new BytesEditorWindowViewModel(Bytes)
            }.ShowDialog<byte[]?>(this);
            vm!.CloseWindow = Close;
        });
    }
}
