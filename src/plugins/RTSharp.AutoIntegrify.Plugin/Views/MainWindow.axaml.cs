using RTSharp.AutoIntegrify.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.AutoIntegrify.Plugin;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
        BindViewModelActions(vm => { });
    }
}