using Avalonia.Controls;
using Avalonia.Input.Platform;

using RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Qbittorrent.Plugin.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();

        this.BindViewModelActions(vm => {
            vm!.CopyToClipboard = CopyToClipboard;
        });
    }

    private async Task CopyToClipboard(string text)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        await topLevel!.Clipboard!.SetTextAsync(text);
    }
}