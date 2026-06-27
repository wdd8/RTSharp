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
    [NotifyCanExecuteChangedFor(nameof(BeginConfigureNewCommand))]
    public partial PluginManifest? SelectedAvailable { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DeleteConfiguredCommand))]
    [NotifyCanExecuteChangedFor(nameof(BeginEditConfiguredCommand))]
    public partial ConfiguredPlugin? SelectedConfigured { get; set; }

    [ObservableProperty]
    public partial bool IsDialogOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(DialogConfirmText))]
    public partial bool IsDialogForNew { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmDialogCommand))]
    public partial string DialogInstanceName { get; set; } = "";

    [ObservableProperty]
    public partial string? DialogServerId { get; set; }

    [ObservableProperty]
    public partial string DialogDataProviderName { get; set; } = "";

    [ObservableProperty]
    public partial bool DialogIsDataProvider { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogColorBrush))]
    public partial Color? DialogColor { get; set; }

    public SolidColorBrush? DialogColorBrush => DialogColor != null ? new SolidColorBrush(DialogColor.Value) : null;

    [ObservableProperty]
    public partial bool HasChanges { get; set; }

    public string DialogTitle => IsDialogForNew ? "Configure new plugin" : "Configure plugin";
    public string DialogConfirmText => IsDialogForNew ? "Create" : "Save";

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

    public bool CanBeginConfigureNew() => SelectedAvailable != null;

    [RelayCommand(CanExecute = nameof(CanBeginConfigureNew))]
    public void BeginConfigureNew()
    {
        IsDialogForNew = true;
        DialogInstanceName = SelectedAvailable!.IsDataProvider ? "" : PathUtils.GetTopMostDirectoryName(SelectedAvailable!.ModuleDir);
        DialogIsDataProvider = SelectedAvailable.IsDataProvider;
        DialogServerId = AvailableServers.FirstOrDefault();
        DialogDataProviderName = "";
        DialogColor = null;
        IsDialogOpen = true;
    }

    public bool CanBeginEditConfigured() => SelectedConfigured != null;

    [RelayCommand(CanExecute = nameof(CanBeginEditConfigured))]
    public void BeginEditConfigured()
    {
        var instance = SelectedConfigured!;
        IsDialogForNew = false;
        DialogInstanceName = instance.InstanceName;
        DialogServerId = instance.ServerId;
        DialogDataProviderName = instance.DataProviderName ?? "";
        DialogIsDataProvider = instance.Manifest.IsDataProvider;
        DialogColor = instance.Color != null ? Color.Parse(instance.Color) : null;
        IsDialogOpen = true;
    }

    public bool CanConfirmDialog() => !string.IsNullOrWhiteSpace(DialogInstanceName);

    [RelayCommand(CanExecute = nameof(CanConfirmDialog))]
    public async Task ConfirmDialog()
    {
        if (IsDialogForNew)
        {
            if (SelectedAvailable == null)
                return;

            try {
                await Plugins.CreatePluginConfig(
                    SelectedAvailable.ModuleDir,
                    DialogInstanceName.Trim(),
                    SelectedAvailable.IsDataProvider,
                    SelectedAvailable.IsDataProvider ? (DialogServerId ?? "") : null,
                    SelectedAvailable.IsDataProvider ? DialogDataProviderName.Trim() : null,
                    SelectedAvailable.IsDataProvider ? DialogColor : null
                );
            } catch (Exception ex) {
                await MessageBoxManager.GetMessageBoxStandard("RT# - Manage plugins", ex.Message, ButtonEnum.Ok, Icon.Stop).ShowWindowAsync();
                return;
            }
        }
        else
        {
            if (SelectedConfigured == null)
                return;

            try {
                await Plugins.UpdatePluginConfig(
                    SelectedConfigured.ConfigPath,
                    DialogInstanceName.Trim(),
                    DialogIsDataProvider ? DialogServerId : null,
                    DialogIsDataProvider ? DialogDataProviderName.Trim() : null,
                    DialogIsDataProvider ? DialogColor : null
                );
            } catch (Exception ex) {
                await MessageBoxManager.GetMessageBoxStandard("RT# - Manage plugins", ex.Message, ButtonEnum.Ok, Icon.Stop).ShowWindowAsync();
                return;
            }
        }

        IsDialogOpen = false;
        HasChanges = true;
        RefreshConfigured();
    }

    [RelayCommand]
    public void CancelDialog() => IsDialogOpen = false;

    public bool CanDeleteConfigured() => SelectedConfigured != null;

    [RelayCommand(CanExecute = nameof(CanDeleteConfigured))]
    public async Task DeleteConfigured()
    {
        var instance = SelectedConfigured!;
        IsDialogOpen = false;

        var box = MessageBoxManager.GetMessageBoxStandard(
            title: "RT# - Delete plugin configuration",
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
