using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.Options;

namespace RTSharp.Views.Options;

public partial class OptionsWindow : VmWindow<OptionsWindowViewModel>
{
    public OptionsWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.FxClose = Close;
        });
    }
}
