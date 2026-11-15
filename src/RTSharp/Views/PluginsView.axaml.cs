using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

namespace RTSharp.Views
{
    public partial class PluginsView : VmWindow<PluginsViewModel>
    {
        public PluginsView()
        {
            InitializeComponent();
        }

        private void EvDoubleTapped(object? sender, TappedEventArgs e)
        {
            if (ViewModel!.PluginsSettingsClickCommand.CanExecute(null))
                ViewModel.PluginsSettingsClickCommand.Execute(null);
        }

        private async void EvCopyInstanceGuid(object? sender, RoutedEventArgs e)
        {
            if (ViewModel!.CurrentlySelectedPlugin is { } plugin && Clipboard != null)
                await Clipboard.SetTextAsync(plugin.InstanceId.ToString());
        }

        private async void EvCopyPluginGuid(object? sender, RoutedEventArgs e)
        {
            if (ViewModel!.CurrentlySelectedPlugin is { } plugin && Clipboard != null)
                await Clipboard.SetTextAsync(plugin.Instance.GUID.ToString());
        }

        private void EvClose(object? sender, RoutedEventArgs e) => Close();
    }
}
