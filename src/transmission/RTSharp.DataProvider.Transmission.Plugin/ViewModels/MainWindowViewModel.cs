using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Transmission.Plugin.Mappers;
using RTSharp.DataProvider.Transmission.Plugin.Models;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin.ViewModels;

public record Category(string Name, string Icon);

public partial class MainWindowViewModel : ObservableObject
{
    public MainWindowViewModel()
    {
        Settings = new Settings();

        currentlySelectedCategory = Categories[0];
    }

    [RelayCommand]
    public async Task SaveSettingsClick()
    {
        SavingSettings = true;
        var client = PluginHost.AttachedDaemonService!.GetGrpcService<GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>();

        Func<ActionQueueAction, Task<Empty>> setSettingsTask = async (task) => await client.SetSessionSettingsAsync(SettingsMapper.MapToProto(Settings), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

        var action = ActionQueueAction.New<Empty>("Set settings", setSettingsTask);
        _ = action.CreateChild("Wait for completion", RUN_MODE.DEPENDS_ON_PARENT, async (parent, action) => {
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

    public required Plugin ThisPlugin { private get; init; }
    public required IPluginHost PluginHost { get; init; }

    public Window ThisWindow { get; set; } = null!; // set right after ctor

    public required string Title { get; init; }

    [ObservableProperty]
    public Settings settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadSelected))]
    [NotifyPropertyChangedFor(nameof(NetworkSelected))]
    [NotifyPropertyChangedFor(nameof(BandwidthSelected))]
    [NotifyPropertyChangedFor(nameof(QueueSelected))]
    public Category currentlySelectedCategory;

    public bool DownloadSelected => CurrentlySelectedCategory?.Name == "Download";
    public bool NetworkSelected => CurrentlySelectedCategory?.Name == "Network (WAN)";
    public bool BandwidthSelected => CurrentlySelectedCategory?.Name == "Bandwidth";
    public bool QueueSelected => CurrentlySelectedCategory?.Name == "Queue";

    public Category[] Categories { get; set; } = new Category[] {
        new Category("Download", "fa7-solid fa7-download"),
        new Category("Network (WAN)", "fa7-solid fa7-network-wired"),
        new Category("Bandwidth", "fa7-solid fa7-signal"),
        new Category("Queue", "fa7-solid fa7-lines-leaning")
    };

    [ObservableProperty]
    public System.Collections.IEnumerable encryptionOptions = new string[] { "Required", "Preferred", "Tolerated" };
}
