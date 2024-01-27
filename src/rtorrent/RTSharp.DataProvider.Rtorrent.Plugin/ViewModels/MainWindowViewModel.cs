using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using RTSharp.DataProvider.Rtorrent.Protocols.Types;
using RTSharp.Shared.Abstractions;
using Settings = RTSharp.DataProvider.Rtorrent.Plugin.Models.Settings;
using RTSharp.DataProvider.Rtorrent.Plugin.Server;
using RTSharp.DataProvider.Rtorrent.Plugin.Mappers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RTSharp.DataProvider.Rtorrent.Plugin.ViewModels
{
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
			var client = Clients.Settings();

			Func<Task<CommandReply>> setSettingsTask = async () => await client.SetSettingsAsync(SettingsMapper.MapToProto(Settings));

			var action = ActionQueueAction.New("Set settings", setSettingsTask);
			_ = action.CreateChild("Wait for completion", RUN_MODE.DEPENDS_ON_PARENT, async parent => {
				SavingSettings = false;
			});
			await ((IActionQueue)ThisPlugin.ActionQueue).RunAction(action);
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
}
