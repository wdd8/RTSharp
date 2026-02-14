using Avalonia.Controls;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Google.Protobuf.WellKnownTypes;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider.Settings;
using RTSharp.DataProvider.Transmission.Plugin.Mappers;
using RTSharp.DataProvider.Transmission.Plugin.Models;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Transmission.Plugin.ViewModels
{
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
            var daemon = PluginHost.AttachedDaemonService;
            var client = daemon.GetGrpcService<GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>();

            Func<Task<Empty>> setSettingsTask = async () => await client.SetSessionSettingsAsync(SettingsMapper.MapToProto(Settings), headers: ThisPlugin.DataProvider.GetBuiltInDataProviderGrpcHeaders());

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
            new Category("Download", "fas fa-download"),
            new Category("Network (WAN)", "fas fa-network-wired"),
            new Category("Bandwidth", "fas fa-signal"),
            new Category("Queue", "fas fa-lines-leaning")
        };

        [ObservableProperty]
        public System.Collections.IEnumerable encryptionOptions = new string[] { "Required", "Preferred", "Tolerated" };
    }
}
