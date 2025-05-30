using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using MsBox.Avalonia.Enums;
using MsBox.Avalonia;

using RTSharp.Shared.Abstractions;

using System;
using System.Linq;
using System.Threading.Tasks;
using RTSharp.ServerScriptPlayground.Plugin.Views;

namespace RTSharp.ServerScriptPlayground.Plugin.ViewModels;

public partial class PlaygroundWindowViewModel(IPluginHost Host, PlaygroundWindow Window) : ObservableObject
{
    [ObservableProperty]
    public string runButtonText = "Run";

    [ObservableProperty]
    public string[] servers = Host.GetDaemonServices().Select(x => x.Id).ToArray();

    [ObservableProperty]
    public string? selectedServer;

    [ObservableProperty]
    public string script = "";

    public Guid? CurrentScriptId { get; set; }

    public IPluginHost Host { get; } = Host;

    [RelayCommand]
    public async Task RunButton()
    {
        if (SelectedServer == null)
            return;

        var server = Host.GetDaemonService(SelectedServer);

        if (CurrentScriptId != null) {
            try { await server.QueueScriptCancellation(CurrentScriptId.Value); } catch { }
            RunButtonText = "Run";
            CurrentScriptId = null;
            return;
        }

        try {
            CurrentScriptId = await server.RunCustomScript(Script, $"PlaygroundScript_{DateTime.UtcNow:o}", new System.Collections.Generic.Dictionary<string, string> { });
        } catch (Exception ex) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Script playground", ex.ToString(), ButtonEnum.Ok, Icon.Stop).ShowWindowDialogAsync(Window);

            return;
        }
        RunButtonText = "Stop";
    }
}
