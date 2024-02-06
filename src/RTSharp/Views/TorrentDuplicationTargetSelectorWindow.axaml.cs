using Avalonia.Controls;

using RTSharp.Plugin;
using RTSharp.Shared.Controls;
using RTSharp.ViewModels;

using System;
using System.Threading.Tasks;

namespace RTSharp.Views;

public partial class TorrentDuplicationTargetSelectorWindow : VmWindow<TorrentDuplicationTargetSelectorWindowViewModel>
{
    public TorrentDuplicationTargetSelectorWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.SelectRemoteDirectoryDialog = SelectRemoteDirectoryDialogAsync;
            vm!.CloseDialog = CloseDialog;
        });
    }

    private void CloseDialog(bool Input)
    {
        Close(Input);
    }

    private async Task<string?> SelectRemoteDirectoryDialogAsync(string? Input)
    {
        var dialog = new DirectorySelectorWindow() {
            ViewModel = new DirectorySelectorWindowViewModel(ViewModel!.SelectedProvider!) {
                WindowTitle = $"RT# - Select directory ({ViewModel!.SelectedProvider.PluginInstance.PluginInstanceConfig.Name})"
            }
        };

        await dialog.ViewModel.SetCurrentFolder(Input);
        var result = await dialog.ShowDialog<string?>(this);
        return result;
    }

    public async void EvDropDownClosed(object sender, EventArgs e)
    {
        await this.ViewModel!.ProviderChanged((DataProvider)((ComboBox)sender).SelectedItem);
    }
}