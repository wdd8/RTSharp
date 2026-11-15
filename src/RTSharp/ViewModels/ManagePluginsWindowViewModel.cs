using Avalonia.Controls;
using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Plugin;
using RTSharp.Shared.Utils;

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.ViewModels;

public partial class ManagePluginsWindowViewModel : ObservableObject
{
    public ObservableCollection<PluginManifest> AvailablePlugins { get; } = [];

    public ObservableCollection<ConfiguredPlugin> ConfiguredPlugins { get; } = [];

    public string[] AvailableServers { get; } = [..Core.Servers.All.Keys];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfigureNewCommand))]
    public partial PluginManifest? SelectedAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConfiguredCommand))]
    [NotifyCanExecuteChangedFor(nameof(EditConfiguredCommand))]
    public partial ConfiguredPlugin? SelectedConfigured { get; set; }

    [ObservableProperty]
    public partial bool HasChanges { get; set; }

    public Func<PluginDialogViewModel, Task<bool>> ShowPluginDialog { get; set; } = null!; // view set

    public ManagePluginsWindowViewModel()
    {
        RefreshModules();
        RefreshConfigured();
    }

    private void RefreshModules()
    {
        var selectedPath = SelectedAvailable?.Module;
        AvailablePlugins.Clear();
        foreach (var module in Plugins.GetAvailablePlugins().OrderBy(x => x.Module))
            AvailablePlugins.Add(module);
        SelectedAvailable = AvailablePlugins.FirstOrDefault(x => x.Module == selectedPath);
    }

    private void RefreshConfigured()
    {
        var selectedName = SelectedConfigured?.InstanceName;
        ConfiguredPlugins.Clear();
        foreach (var instance in Plugins.ListPluginConfigurations().OrderBy(x => x.InstanceName))
            ConfiguredPlugins.Add(instance);
        SelectedConfigured = ConfiguredPlugins.FirstOrDefault(x => x.InstanceName == selectedName);
    }

    public bool CanConfigureNew() => SelectedAvailable != null;

    [RelayCommand(CanExecute = nameof(CanConfigureNew))]
    public async Task ConfigureNew()
    {
        var module = SelectedAvailable!;
        var dialog = new PluginDialogViewModel {
            AvailableServers = AvailableServers,
            IsDataProvider = module.IsDataProvider,
            ModuleDir = module.ModuleDir,
            InstanceName = module.IsDataProvider ? "" : PathUtils.GetTopMostDirectoryName(module.ModuleDir),
            ServerId = AvailableServers.FirstOrDefault()
        };

        if (!await ShowPluginDialog(dialog))
            return;

        HasChanges = true;
        RefreshConfigured();
    }

    public bool CanEditConfigured() => SelectedConfigured != null;

    [RelayCommand(CanExecute = nameof(CanEditConfigured))]
    public async Task EditConfigured()
    {
        var instance = SelectedConfigured!;
        var dialog = new PluginDialogViewModel {
            AvailableServers = AvailableServers,
            IsDataProvider = instance.Manifest.IsDataProvider,
            ConfigPath = instance.ConfigPath,
            InstanceName = instance.InstanceName,
            ServerId = instance.ServerId,
            DataProviderName = instance.DataProviderName ?? "",
            DataProviderColor = instance.Color != null ? Color.Parse(instance.Color) : null
        };

        if (!await ShowPluginDialog(dialog))
            return;

        HasChanges = true;
        RefreshConfigured();
    }

    public bool CanDeleteConfigured() => SelectedConfigured != null;

    [RelayCommand(CanExecute = nameof(CanDeleteConfigured))]
    public async Task DeleteConfigured()
    {
        var instance = SelectedConfigured!;

        var box = MessageBoxManager.GetMessageBoxStandard(
            title: "RT# - Manage Plugins",
            text: $"Delete configuration \"{instance.InstanceName}\"?\n\n{instance.ConfigPath}",
            @enum: ButtonEnum.YesNo,
            icon: Icon.Warning,
            windowStartupLocation: WindowStartupLocation.CenterScreen);

        if (await box.ShowAsync() != ButtonResult.Yes)
            return;

        Plugins.DeletePluginConfig(instance.ConfigPath);
        HasChanges = true;
        RefreshConfigured();
    }
}
