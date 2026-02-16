using RTSharp.DataProvider.Transmission.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }
}