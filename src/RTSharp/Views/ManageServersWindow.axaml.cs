using Avalonia.Input;
using Avalonia.Interactivity;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views;

public partial class ManageServersWindow : VmWindow<ManageServersWindowViewModel>
{
    public ManageServersWindow()
    {
        InitializeComponent();

        BindViewModelActions(vm => {
            vm!.ShowServerDialog = async dialog => await new ServerDialog { ViewModel = dialog }.ShowDialog<bool>(this);
        });
    }

    private void EvDoubleTapped(object sender, TappedEventArgs e)
    {
        if (ViewModel!.SelectedServer != null)
            ViewModel.EditCommand.Execute(null);
    }

    private void EvClose(object? sender, RoutedEventArgs e) => Close();
}
