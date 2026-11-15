using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class PluginDialog : VmWindow<PluginDialogViewModel>
{
    public PluginDialog()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.CloseWithResult = result => Close(result);
        });
    }
}
