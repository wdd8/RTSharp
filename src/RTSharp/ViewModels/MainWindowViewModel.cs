using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using RTSharp.ViewModels.TorrentListing;
using RTSharp.Views;
using RTSharp.Views.Options;
using RTSharp.Views.Statistics;
using RTSharp.Views.Tools;

namespace RTSharp.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        public ObservableCollection<MenuItem> MenuItems { get; } = new();

        public ObservableCollection<Plugin.PluginInstance> Plugins => Plugin.Plugins.LoadedPlugins;

        public ICommand CmdOptionsClick { get; }

        public Func<PluginsViewModel, Task> ShowPluginsDialog { get; set; }

        public Func<ServersWindowViewModel, Task> ShowServersDialog { get; set; }

        [ObservableProperty]
        public string stringFilter;

        [ObservableProperty]
        public string title = "RT#";

        public MainWindowViewModel()
        {
            ((AppViewModel)Avalonia.Application.Current.DataContext).PropertyChanged += (sender, e) => {
                Title = ((AppViewModel)sender).TrayIconText;
            };
        }

        [RelayCommand]
        public async Task AboutFrameworkClick()
        {
            var wnd = new AboutAvaloniaDialog();
            await wnd.ShowDialog(App.MainWindow);
        }

        [RelayCommand]
        public void OptionsClick()
        {
            var optionsWindow = new OptionsWindow() {
                DataContext = new()
            };
            optionsWindow.Show();
        }

        [RelayCommand]
        public void AboutClick()
        {
            var aboutWindow = new AboutWindow();
            aboutWindow.Show();
        }

        [RelayCommand]
        public void AddTorrentClick()
        {
            var addTorrentWindow = new AddTorrentWindow() {
                ViewModel = new()
            };
            addTorrentWindow.Show();
        }

        [RelayCommand]
        public void PluginsClick()
        {
            var vm = new PluginsViewModel();
            ShowPluginsDialog(vm);
        }

        [RelayCommand]
        public void ServersClick()
        {
            var vm = new ServersWindowViewModel();
            ShowServersDialog(vm);
        }

        [RelayCommand]
        public void TorrentCreatorClick()
        {
            var torrentCreatorWindow = new TorrentCreatorWindow() {
                ViewModel = new()
            };
            torrentCreatorWindow.Show();
        }

        [RelayCommand]
        public void AddTorrentListingTabClick()
        {
            var curCount = App.DockManager.DockItemsViewModels.Where(x => x.DockId.StartsWith("TorrentListing")).Count();

            var vm = new DockTorrentListingViewModel() {
                DockId = "TorrentListing" + curCount,
                DefaultDockGroupId = "MainGroup",
                TheVM = new TorrentListingViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "TorrentListingTemplate",
                IsPredefined = false,
                IsSelected = true
            };

            App.DockManager.DockItemsViewModels.Add(vm);
        }

        [RelayCommand]
        public void AddActionQueueTabClick()
        {
            var curCount = App.DockManager.DockItemsViewModels.Where(x => x.DockId.StartsWith("ActionQueue")).Count();

            var vm = new DockActionQueuesViewModel() {
                DockId = "ActionQueue" + curCount,
                DefaultDockGroupId = "MainGroup",
                TheVM = new ActionQueuesViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "ActionQueueTemplate",
                IsPredefined = false,
                IsSelected = true
            };

            App.DockManager.DockItemsViewModels.Add(vm);
        }

        [RelayCommand]
        public void AddLogTabClick()
        {
            var curCount = App.DockManager.DockItemsViewModels.Where(x => x.DockId.StartsWith("LogEntries")).Count();

            var vm = new DockLogEntriesViewModel() {
                DockId = "LogEntries" + curCount,
                DefaultDockGroupId = "MainGroup",
                TheVM = new LogEntriesViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "LogEntriesTemplate",
                IsPredefined = false,
                IsSelected = true
            };

            App.DockManager.DockItemsViewModels.Add(vm);
        }

        [RelayCommand]
        public void AddDataProvidersTabClick()
        {
            var curCount = App.DockManager.DockItemsViewModels.Where(x => x.DockId.StartsWith("DataProviders")).Count();

            var vm = new DockDataProvidersViewModel() {
                DockId = "DataProviders" + curCount,
                DefaultDockGroupId = "MainGroup",
                TheVM = new DataProvidersViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "DataProvidersTemplate",
                IsPredefined = false,
                IsSelected = true
            };

            App.DockManager.DockItemsViewModels.Add(vm);
        }

        [RelayCommand]
        public void ShowStatisticsWindow()
        {
            var statsWindow = new StatisticsWindow() {
                ViewModel = new()
            };

            statsWindow.Show();
        }
    }
}
