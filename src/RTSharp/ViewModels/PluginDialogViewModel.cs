using System;
using System.Threading.Tasks;

using Avalonia.Media;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Plugin;

namespace RTSharp.ViewModels;

/// <summary>
/// Configure New Plugin and Configure Plugin dialog
/// </summary>
public partial class PluginDialogViewModel : ObservableObject
{
    public required string[] AvailableServers { get; init; }

    public required bool IsDataProvider { get; init; }

    public string? ModuleDir { get; init; }

    public string? ConfigPath { get; init; }

    public bool IsNew => ConfigPath == null;

    public string Title => IsNew ? "RT# - Configure New Plugin" : "RT# - Configure Plugin";

    public string ConfirmText => IsNew ? "Create" : "OK";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial string InstanceName { get; set; } = "";

    [ObservableProperty]
    public partial string? ServerId { get; set; }

    [ObservableProperty]
    public partial string DataProviderName { get; set; } = "";

    [ObservableProperty]
    public partial Color? DataProviderColor { get; set; }

    public Action<bool> CloseWithResult { get; set; } = null!; // view set

    public bool CanConfirm() => !String.IsNullOrWhiteSpace(InstanceName);

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    public async Task Confirm()
    {
        try {
            if (IsNew) {
                await Plugins.CreatePluginConfig(
                    ModuleDir!,
                    InstanceName.Trim(),
                    IsDataProvider,
                    IsDataProvider ? (ServerId ?? "") : null,
                    IsDataProvider ? DataProviderName.Trim() : null,
                    IsDataProvider ? DataProviderColor : null
                );
            } else {
                await Plugins.UpdatePluginConfig(
                    ConfigPath!,
                    InstanceName.Trim(),
                    IsDataProvider ? ServerId : null,
                    IsDataProvider ? DataProviderName.Trim() : null,
                    IsDataProvider ? DataProviderColor : null
                );
            }
        } catch (Exception ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Manage Plugins", ex.Message, ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
            return;
        }

        CloseWithResult(true);
    }

    [RelayCommand]
    public void Cancel() => CloseWithResult(false);
}
