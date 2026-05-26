using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using DynamicData;

using MsBox.Avalonia;

using RTSharp.Plugin;
using RTSharp.Shared.Controls;
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
    public ObservableCollection<RTSharpPlugin> ActivePlugins => Plugins.LoadedPlugins;

    public ObservableCollection<string> UnloadedPluginDirs { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadClickCommand))]
    public partial string? SelectedUnloadedDir { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnloadClickCommand))]
    public partial RTSharpPlugin? SelectedActivePlugin { get; set; }

    public ManagePluginsWindowViewModel()
    {
        RepopulateUnloadedPlugins();
    }

    public bool CanExecuteLoad() => !String.IsNullOrEmpty(SelectedUnloadedDir);

    [RelayCommand(CanExecute = nameof(CanExecuteLoad))]
    public async Task LoadClick()
    {
        var existingConfig = Plugins.GetFirstPluginConfigOrDefault(Path.GetFullPath(Path.Combine(Shared.Abstractions.Consts.PLUGINS_PATH, SelectedUnloadedDir!)));
        string? config = null;
        if (existingConfig != null)
            config = existingConfig;
        else
            config = await Plugins.GeneratePluginConfig(SelectedUnloadedDir!);

        var wBox = new WaitingBox($"Loading {SelectedUnloadedDir}", $"Loading {config}...", BuiltInIcons.VISTA_WAIT);
        wBox.Show();

        try {
            await Plugins.LoadPlugin(config, ((string Status, float Percentage) e) => {
                wBox.Report(((int)e.Percentage, e.Status));
            });
        } catch (Exception ex) {
            var msgBox = MessageBoxManager.GetMessageBoxStandard(
                title: "RT# - Failed to load plugin", 
                text: $"Failed to load plugin {config}\n{ex}", 
                @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok, 
                icon: MsBox.Avalonia.Enums.Icon.Stop, 
                windowStartupLocation: Avalonia.Controls.WindowStartupLocation.CenterOwner);
            await msgBox.ShowAsync();
        } finally {
            RepopulateUnloadedPlugins();
            wBox.Close();
        }
    }

    public bool CanExecuteUnload() => SelectedActivePlugin != null;

    [RelayCommand(CanExecute = nameof(CanExecuteUnload))]
    public async Task UnloadClick()
    {
        await SelectedActivePlugin!.Unload();
        RepopulateUnloadedPlugins();
    }

    public void RepopulateUnloadedPlugins()
    {
        UnloadedPluginDirs.Clear();
        UnloadedPluginDirs.AddRange(Plugins.ListUnloadedPluginDirs().Select(x => Path.GetFileName(x)!));
    }
}
