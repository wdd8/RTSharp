using RTSharp.CrossSeed.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.CrossSeed.Plugin;

public partial class SettingsWindow : VmWindow<SettingsWindowViewModel>
{
    public SettingsWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.FxClose = Close;
        });
    }
}
