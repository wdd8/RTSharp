using RTSharp.PeerReputation.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.PeerReputation.Plugin;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();

        // For OnContextPopulated
        BindViewModelActions(vm => { });
    }
}
