using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

namespace RTSharp.ViewModels
{
    public partial class PluginsViewModel : ObservableObject
    {
        public ObservableCollection<RTSharpPlugin> Plugins => Plugin.Plugins.LoadedPlugins;
        
        private RTSharpPlugin _currentlySelectedPlugin;
        public RTSharpPlugin CurrentlySelectedPlugin {
            get {
                return _currentlySelectedPlugin;
            }
            set {
                this.SetProperty(ref _currentlySelectedPlugin, value, nameof(CurrentlySelectedPlugin));
                if (value == null) {
                    PluginInfo = null;
                    return;
                }

                PluginInfo = new Models.PluginInfo() {
                    InstanceGuid = value.InstanceId.ToString(),
                    DisplayName = value.PluginInstanceConfig.Name,
                    Description = value.Instance.Description,
                    Author = value.Instance.Author,
                    Version = value.Instance.Version.VersionDisplayString,
                    PluginGuid = value.Instance.GUID.ToString()
                };
                PluginsSettingsClickCommand.NotifyCanExecuteChanged();
            }
        }

        [ObservableProperty]
        public partial Models.PluginInfo PluginInfo { get; set; }
        public Window ThisWindow { get; set; }

        public PluginsViewModel()
        {
            PluginInfo = new Models.PluginInfo() {
                InstanceGuid = "N/A",
                DisplayName = "N/A",
                Description = "N/A",
                Author = "N/A",
                Version = "N/A",
                PluginGuid = "N/A"
            };

            if (Plugins.Count > 0)
                CurrentlySelectedPlugin = Plugins[0];
        }

        public bool CanExecutePluginsSettingsClick() => ((IPlugin)CurrentlySelectedPlugin.Instance).Capabilities.HasSettingsWindow;
        [RelayCommand(CanExecute = nameof(CanExecutePluginsSettingsClick))]
        public async Task PluginsSettingsClick()
        {
            try {
                await ((IPlugin)CurrentlySelectedPlugin.Instance).ShowPluginSettings(ThisWindow);
            } catch (Exception ex) {
                var msgBox = MessageBoxManager.GetMessageBoxStandard("Plugin manager", $"Failed to show plugin settings:\n{ex}", ButtonEnum.Ok, Icon.Error, WindowStartupLocation.CenterScreen);
                await msgBox.ShowWindowDialogAsync(ThisWindow);
            }
        }

        [RelayCommand]
        public void ManagePluginsClick()
        {
            var wnd = new ManagePluginsWindow {
                ViewModel = new()
            };
            wnd.Show();
        }
    }
}
