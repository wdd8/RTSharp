using Avalonia.Controls;

using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.ViewModels;

using System.Threading.Tasks;

namespace RTSharp.Views;

public partial class DuplicationTargetSelectorControl : UserControl
{
    public DuplicationTargetSelectorControl()
    {
        InitializeComponent();
    }

    public static async Task<string?> DefaultSelectRemoteDirectoryDialogImpl(Window Owner, IDataProvider? SelectedProvider, string? Input)
    {
        if (SelectedProvider == null)
            return null;

        var dialog = new DirectorySelectorWindow() {
            ViewModel = new DirectorySelectorWindowViewModel(SelectedProvider) {
                WindowTitle = $"RT# - Select directory ({SelectedProvider.Host.Config.Plugin.Name})"
            }
        };

        await dialog.ViewModel.SetCurrentFolder(Input);
        return await dialog.ShowDialog<string?>(Owner);
    }
}
