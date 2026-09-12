using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia.Enums;
using MsBox.Avalonia;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.ServerScriptPlayground.Plugin.Views;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.ServerScriptPlayground.Plugin.ViewModels;

public partial class PlaygroundWindowViewModel(IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host, PlaygroundWindow Window, Guid OwnerId) : ObservableObject
{
    [ObservableProperty]
    public partial string RunButtonText { get; set; } = "Run";

    [ObservableProperty]
    public partial string[] Servers { get; set; } = Host.GetDaemonServices().Select(x => x.Id).ToArray();

    [ObservableProperty]
    public partial string? SelectedServer { get; set; }

    [ObservableProperty]
    public partial string Script { get; set; } = "";

    [ObservableProperty]
    public partial string ServiceId { get; set; } = "";

    public ObservableCollection<InstalledScript> InstalledScripts { get; } = new();

    public Guid? CurrentScriptId { get; set; }

    public IPluginHost<PluginConfigContainer<PluginInstanceConfig>> Host { get; } = Host;

    partial void OnSelectedServerChanged(string? value)
    {
        _ = RefreshInstalled();
    }

    [RelayCommand]
    public async Task RunButton()
    {
        if (SelectedServer == null)
            return;

        var server = Host.GetDaemonService(SelectedServer)!;

        if (CurrentScriptId != null) {
            try { await server.QueueScriptCancellation(CurrentScriptId.Value); } catch { }
            RunButtonText = "Run";
            CurrentScriptId = null;
            return;
        }

        try {
            CurrentScriptId = await server.RunCustomScript(Script, $"PlaygroundScript_{DateTime.UtcNow.Ticks}", []);
        } catch (Exception ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Script playground", ex.ToString(), ButtonEnum.Ok, Icon.Stop).ShowWindowDialogAsync(Window);

            return;
        }
        RunButtonText = "Stop";
    }

    [RelayCommand]
    public async Task InstallService()
    {
        if (SelectedServer == null)
            return;

        if (String.IsNullOrWhiteSpace(ServiceId)) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Script playground", "Enter a service id to install under.", ButtonEnum.Ok, Icon.Warning).ShowWindowDialogAsync(Window);
            return;
        }

        try {
            await Host.GetDaemonService(SelectedServer)!.InstallScript(ServiceId, ServiceId, OwnerId, Script, []);
        } catch (Exception ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Script playground", ex.ToString(), ButtonEnum.Ok, Icon.Stop).ShowWindowDialogAsync(Window);
            return;
        }

        await RefreshInstalled();
    }

    [RelayCommand]
    public async Task UninstallService(InstalledScript Script)
    {
        if (SelectedServer == null)
            return;

        try {
            await Host.GetDaemonService(SelectedServer)!.UninstallScript(Script.Id);
        } catch (Exception ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Script playground", ex.ToString(), ButtonEnum.Ok, Icon.Stop).ShowWindowDialogAsync(Window);
            return;
        }

        await RefreshInstalled();
    }

    [RelayCommand]
    public async Task RefreshInstalled()
    {
        InstalledScripts.Clear();

        if (SelectedServer == null)
            return;

        IReadOnlyList<InstalledScript> installed;
        try {
            installed = await Host.GetDaemonService(SelectedServer)!.ListInstalledScripts();
        } catch (Exception ex) {
            Host.Logger.Error(ex, "Could not list installed scripts on {Server}", SelectedServer);
            return;
        }

        foreach (var script in installed)
            InstalledScripts.Add(script);
    }
}
