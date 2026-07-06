using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

using System.Threading.Tasks;

namespace RTSharp.Views;

public partial class TorrentDuplicationTargetSelectorWindow : VmWindow<TorrentDuplicationTargetSelectorWindowViewModel>
{
    public TorrentDuplicationTargetSelectorWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.Selector.SelectRemoteDirectoryDialog = SelectRemoteDirectoryDialogAsync;
            vm!.CloseDialog = CloseDialog;
        });
    }

    private void CloseDialog(bool Input)
    {
        Close(Input);
    }

    private Task<string?> SelectRemoteDirectoryDialogAsync(string? Input) =>
        DuplicationTargetSelectorControl.DefaultSelectRemoteDirectoryDialogImpl(this, ViewModel!.Selector.SelectedProvider, Input);
}