using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using MsBox.Avalonia;

using RTSharp.Core.Util;
using RTSharp.Plugin;
using RTSharp.Shared.Controls.Views;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels;

public partial class ManagePluginsWindowViewModel : ObservableObject
{
    public ObservableCollection<PluginInstance> ActivePlugins => Plugins.LoadedPlugins;

    public ObservableCollectionEx<string> UnloadedPluginDirs { get; } = new();

    [ObservableProperty]
    public string selectedUnloadedDir;

    [ObservableProperty]
    public PluginInstance selectedActivePlugin;

    public ManagePluginsWindowViewModel()
    {
        UnloadedPluginDirs.AddRange(Plugins.ListUnloadedPluginDirs().Select(Path.GetFileName));
    }

    [RelayCommand]
    public async Task LoadClick()
    {
        var existingConfig = Plugins.GetFirstPluginConfigOrDefault(Path.GetFullPath(Path.Combine(Shared.Abstractions.Consts.PLUGINS_PATH, SelectedUnloadedDir)));
        string config = null;
        if (existingConfig != null)
            config = existingConfig;
        else
            config = await Plugins.GeneratePluginConfig(SelectedUnloadedDir);

        var wBox = new WaitingBox($"Loading {SelectedUnloadedDir}", $"Loading {config}...", Shared.Controls.ViewModels.WAITING_BOX_ICON.VISTA_WAIT);
        wBox.Show();

        try {
            await Plugins.LoadPlugin(config, new Progress<(string Status, float Percentage)>(e => {
                wBox.Report(((int)e.Percentage, e.Status));
            }));
        } catch (Exception ex) {
            var msgBox = MessageBoxManager.GetMessageBoxStandard("RT# - Failed to load plugin", $"Failed to load plugin {config}\n{ex}", MsBox.Avalonia.Enums.ButtonEnum.Ok, MsBox.Avalonia.Enums.Icon.Stop, Avalonia.Controls.WindowStartupLocation.CenterOwner);
            await msgBox.ShowAsync();
        } finally {
            UnloadedPluginDirs.Replace(Plugins.ListUnloadedPluginDirs().Select(Path.GetFileName));
            wBox.Close();
        }
    }

    [RelayCommand]
    public async Task UnloadClick()
    {
        await SelectedActivePlugin.Unload();
        UnloadedPluginDirs.Replace(Plugins.ListUnloadedPluginDirs().Select(Path.GetFileName));
    }
}
