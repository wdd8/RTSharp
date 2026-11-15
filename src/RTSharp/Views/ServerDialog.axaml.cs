using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class ServerDialog : VmWindow<ServerDialogViewModel>
{
    public ServerDialog()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.CloseWithResult = result => Close(result);
        });
    }
}
