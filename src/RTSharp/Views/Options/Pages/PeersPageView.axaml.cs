using Avalonia.Controls;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels.Options.Pages;

namespace RTSharp.Views.Options.Pages
{
    public partial class PeersPageView : VmUserControl<PeersPageViewModel>
    {
        public PeersPageView()
        {
            InitializeComponent();

            BindViewModelActions(vm => {
                vm!.PromptText = (title, fields) => TextInputDialog.Prompt((Window)TopLevel.GetTopLevel(this)!, title, fields);
            });
        }
    }
}
