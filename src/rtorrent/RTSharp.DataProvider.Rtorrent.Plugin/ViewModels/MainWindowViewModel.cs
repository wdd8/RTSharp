using Avalonia.Controls;
using RTSharp.Shared.Abstractions;
using Settings = RTSharp.DataProvider.Rtorrent.Plugin.Models.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions.Client;
using Serilog;

namespace RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;

public record Category(string Name, string Icon);

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        CurrentlySelectedCategory = Categories[0];
    }

    [RelayCommand]
    public async Task SaveSettingsClick()
    {
        SavingSettings = true;
        var client = PluginHost.AttachedDaemonService!.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

        Func<ActionQueueAction, Task<CommandReply>> setSettingsTask = async (task) => await client.SetSettingsAsync(SettingsMapper.MapToProto(Settings), headers: ThisPlugin.Host.GetBuiltInDataProviderGrpcHeaders());

        var action = ActionQueueAction.New("Set settings", setSettingsTask);
        _ = action.CreateChild("Wait for completion", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
            SavingSettings = false;
        });

        try {
            await ((IActionQueueRenderer)ThisPlugin.ActionQueue).RunAction(action);
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to save settings");
        }
        await PluginHost.SavePluginConfig(cfg => cfg.ListUpdateInterval = TimeSpan.FromSeconds(ListUpdateIntervalSeconds));
    }

    async partial void OnSavingSettingsChanged(bool value)
    {
        if (!value) {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        SaveSettingsEnabled = !value;
    }

    [ObservableProperty]
    public partial bool SavingSettings { get; set; }

    [ObservableProperty]
    public partial bool SaveSettingsEnabled { get; set; } = true;

    public required Plugin ThisPlugin { private get; init; }
    public required IDataProviderHost<RtorrentDataProviderInstanceConfig> PluginHost { get; init; }

    public Window ThisWindow { get; set; } = null!; // init set

    public required string Title { get; init; }

    [ObservableProperty]
    public required partial Settings Settings { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneralSelected))]
    [NotifyPropertyChangedFor(nameof(PeersSelected))]
    [NotifyPropertyChangedFor(nameof(ConnectionSelected))]
    [NotifyPropertyChangedFor(nameof(AdvancedSelected))]
    [NotifyPropertyChangedFor(nameof(RTSharpSelected))]
    public partial Category CurrentlySelectedCategory { get; set; }

    public bool GeneralSelected => CurrentlySelectedCategory?.Name == "General";
    public bool PeersSelected => CurrentlySelectedCategory?.Name == "Peers";
    public bool ConnectionSelected => CurrentlySelectedCategory?.Name == "Connection";
    public bool AdvancedSelected => CurrentlySelectedCategory?.Name == "Advanced";
    public bool RTSharpSelected => CurrentlySelectedCategory?.Name == "RT#";

    [ObservableProperty]
    public partial double ListUpdateIntervalSeconds { get; set; } = 1;

    public Category[] Categories { get; set; } = new Category[] {
        new Category("General", "fa7-solid fa7-wrench"),
        new Category("Peers", "fa7-solid fa7-download"),
        new Category("Connection", "fa7-solid fa7-signal"),
        new Category("Advanced", "fa7-solid fa7-tools"),
        new Category("RT#", "fa7-solid fa7-stopwatch")
    };
}
