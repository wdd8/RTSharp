using RTSharp.DataProvider.Qbittorrent.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Qbittorrent.Plugin.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }
}