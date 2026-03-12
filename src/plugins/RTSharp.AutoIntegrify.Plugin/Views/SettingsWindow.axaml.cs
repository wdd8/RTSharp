using RTSharp.AutoIntegrify.Plugin.ViewModels;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.AutoIntegrify.Plugin;

public partial class SettingsWindow : VmWindow<SettingsWindowViewModel>
{
    public SettingsWindow()
    {
        InitializeComponent();
        this.BindViewModelActions(vm => { });
    }
}