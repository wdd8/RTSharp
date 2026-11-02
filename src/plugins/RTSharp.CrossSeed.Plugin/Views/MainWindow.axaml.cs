using RTSharp.CrossSeed.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.CrossSeed.Plugin;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => vm!.Owner = this);
    }
}
