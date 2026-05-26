using Avalonia.Controls;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions.Client;
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
        if (ViewModel!.SelectedProvider == null)
            return null;

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
        var selectedItem = (RTSharpDataProvider?)((ComboBox)sender).SelectedItem;

        if (selectedItem == null)
            return;

        await this.ViewModel!.ProviderChanged(selectedItem);
    }
}