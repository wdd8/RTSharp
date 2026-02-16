using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using RTSharp.Shared.Abstractions;
using Settings = RTSharp.DataProvider.Rtorrent.Plugin.Models.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Rtorrent.Plugin.ViewModels;

public record Category(string Name, string Icon);

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        currentlySelectedCategory = Categories[0];
    }

    [RelayCommand]
    public async Task SaveSettingsClick()
    {
        SavingSettings = true;
        var daemon = PluginHost.AttachedDaemonService;
        var client = daemon.GetGrpcService<GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>();

        Func<Task<CommandReply>> setSettingsTask = async () => await client.SetSettingsAsync(SettingsMapper.MapToProto(Settings), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

        var action = ActionQueueAction.New("Set settings", setSettingsTask);
        _ = action.CreateChild("Wait for completion", RUN_MODE.DEPENDS_ON_PARENT, async parent => {
            SavingSettings = false;
        });
        await ((IActionQueueRenderer)ThisPlugin.ActionQueue).RunAction(action);
    }

    async partial void OnSavingSettingsChanged(bool value)
    {
        if (!value) {
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        SaveSettingsEnabled = !value;
    }

    [ObservableProperty]
    public bool savingSettings;

    [ObservableProperty]
    public bool saveSettingsEnabled = true;

    public Plugin ThisPlugin { private get; init; }
    public IPluginHost PluginHost { get; init; }

    public Window ThisWindow { get; set; }

    public string Title { get; init; }

    [ObservableProperty]
    public Settings settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GeneralSelected))]
    [NotifyPropertyChangedFor(nameof(PeersSelected))]
    [NotifyPropertyChangedFor(nameof(ConnectionSelected))]
    [NotifyPropertyChangedFor(nameof(AdvancedSelected))]
    public Category currentlySelectedCategory;

    public bool GeneralSelected => CurrentlySelectedCategory?.Name == "General";
    public bool PeersSelected => CurrentlySelectedCategory?.Name == "Peers";
    public bool ConnectionSelected => CurrentlySelectedCategory?.Name == "Connection";
    public bool AdvancedSelected => CurrentlySelectedCategory?.Name == "Advanced";

    public Category[] Categories { get; set; } = new Category[] {
        new Category("General", "fas fa-wrench"),
        new Category("Peers", "fas fa-download"),
        new Category("Connection", "fas fa-signal"),
        new Category("Advanced", "fas fa-tools")
    };
}
