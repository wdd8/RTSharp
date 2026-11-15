using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Plugin;

namespace RTSharp.ViewModels
{
    public partial class PluginsViewModel : ObservableObject
    {
        public ObservableCollection<RTSharpPlugin> Plugins => Plugin.Plugins.LoadedPlugins;
        
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(PluginsSettingsClickCommand))]
        public partial RTSharpPlugin? CurrentlySelectedPlugin { get; set; }

        public required Window ThisWindow { get; set; }

        public PluginsViewModel()
        {
            if (Plugins.Count > 0)
                CurrentlySelectedPlugin = Plugins[0];
        }

        public bool CanExecutePluginsSettingsClick() => CurrentlySelectedPlugin != null && CurrentlySelectedPlugin.Instance.Capabilities.HasSettingsWindow;
        [RelayCommand(CanExecute = nameof(CanExecutePluginsSettingsClick))]
        public async Task PluginsSettingsClick()
        {
            try {
                await CurrentlySelectedPlugin!.Instance.ShowPluginSettings(ThisWindow);
            } catch (Exception ex) {
                var msgBox = MessageBoxManager.GetMessageBoxStandard(
                    title: "RT# - Plugins",
                    text: $"Unable to show plugin settings:\n{ex}",
                    @enum: ButtonEnum.Ok,
                    icon: Icon.Error,
                    windowStartupLocation: WindowStartupLocation.CenterOwner);
                await msgBox.ShowWindowDialogAsync(ThisWindow);
            }
        }

        [RelayCommand]
        public void ManagePluginsClick()
        {
            var wnd = new ManagePluginsWindow {
                ViewModel = new()
            };
            wnd.Show(App.MainWindow);
        }
    }
}
