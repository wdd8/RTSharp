using Avalonia.Controls;
using Avalonia.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp;

public partial class ManagePluginsWindow : VmWindow<ManagePluginsWindowViewModel>
{
    private bool RestartNotificationShown;

    public ManagePluginsWindow()
    {
        InitializeComponent();
        Closing += EvClosing;
    }

    private async void EvClosing(object? sender, WindowClosingEventArgs e)
    {
        if (RestartNotificationShown || !ViewModel!.HasChanges)
            return;

        e.Cancel = true;
        await MessageBoxManager.GetMessageBoxStandard(
            title: "RT# - Manage Plugins",
            text: "Plugin configuration has changed. Please restart the application to apply changes.",
            @enum: ButtonEnum.Ok,
            icon: MsBox.Avalonia.Enums.Icon.Info,
            windowStartupLocation: WindowStartupLocation.CenterOwner
        ).ShowWindowDialogAsync(this);

        RestartNotificationShown = true;
        Close();
    }

    private void EvDoubleTapped(object sender, TappedEventArgs e)
    {
        if (ViewModel!.SelectedConfigured != null)
            ViewModel.BeginEditConfiguredCommand.Execute(null);
    }
}