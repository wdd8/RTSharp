using RTSharp.MassTrackerRewrite.Plugin.ViewModels;
using RTSharp.Shared.Controls;

namespace RTSharp.MassTrackerRewrite.Plugin;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
        BindViewModelActions(vm => {});
    }
}