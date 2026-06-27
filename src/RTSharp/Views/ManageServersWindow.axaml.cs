using Avalonia.Input;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class ManageServersWindow : VmWindow<ManageServersWindowViewModel>
{
    public ManageServersWindow()
    {
        InitializeComponent();
    }

    private void EvDoubleTapped(object sender, TappedEventArgs e)
    {
        if (ViewModel!.SelectedServer != null)
            ViewModel.BeginEditCommand.Execute(null);
    }
}
