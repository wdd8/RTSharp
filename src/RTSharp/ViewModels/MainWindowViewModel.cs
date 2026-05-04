using Avalonia.Controls;
using Avalonia.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Avalonia.Controls;
using Dock.Model.Avalonia;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core;
using RTSharp.ViewModels.Statistics;
using RTSharp.ViewModels.TorrentListing;
using RTSharp.Views;
using RTSharp.Views.Options;
using RTSharp.Views.Statistics;
using RTSharp.Views.Tools;
using RTSharp.Views.TorrentListing;
using RTSharp.Views.Util;

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace RTSharp.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly TorrentListingViewModel TorrentListingViewModel;

        public ObservableCollection<MenuItem> MenuItems { get; } = new();

        public ObservableCollection<Plugin.RTSharpPlugin> Plugins => Plugin.Plugins.LoadedPlugins;

        public ICommand CmdOptionsClick { get; }

        public Func<PluginsViewModel, Task> ShowPluginsDialog { get; set; }

        public Func<ServersWindowViewModel, Task> ShowServersDialog { get; set; }

        [ObservableProperty]
        public partial string StringFilter { get; set; }

        [ObservableProperty]
        public partial string Title { get; set; } = "RT#";

        public IFactory DockFactory { get; }

        public RootDock RootDock { get; }

        public IReadOnlyDictionary<string, IDockable> DockableRegistry { get; }

        public MainWindowViewModel()
        {
            ((AppViewModel)Avalonia.Application.Current.DataContext).PropertyChanged += (sender, e) => {
                Title = ((AppViewModel)sender).TrayIconText;
            };

            DockFactory = new RTSharpDockFactory();

            TorrentListingViewModel = new TorrentListingViewModel();
            var torrentListingView = new TorrentListingView {
                DataContext = TorrentListingViewModel
            };

            using var scope = Core.ServiceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
            torrentListingView.RestoreGridState(config.UIState.Value.TorrentGridState);

            var torrentListing = new DocumentWithIcon {
                Id = "TorrentListing",
                Title = "Torrent listing",
                Icon = FontAwesomeIcons.Get("fa7-solid fa7-list"),
                Content = new Func<IServiceProvider, object>(_ => torrentListingView),
                DockCapabilityOverrides = new()
            };

            var actionQueuesView = new ActionQueuesView {
                DataContext = new ActionQueuesViewModel()
            };
            var actionQueue = new DocumentWithIcon {
                Id = "ActionQueue",
                Title = "Action queue",
                Icon = FontAwesomeIcons.Get("fa7-solid fa7-bolt"),
                Content = new Func<IServiceProvider, object>(_ => actionQueuesView),
                DockCapabilityOverrides = new()
            };

            var logEntriesView = new LogEntriesView {
                DataContext = new LogEntriesViewModel()
            };
            var log = new DocumentWithIcon {
                Id = "Log",
                Title = "Log",
                Icon = FontAwesomeIcons.Get("fa7-solid fa7-calendar-days"),
                Content = new Func<IServiceProvider, object>(_ => logEntriesView),
                DockCapabilityOverrides = new()
            };

            var dataProvidersView = new DataProvidersView() {
                DataContext = new DataProvidersViewModel()
            };
            var rightTool = new Tool {
                Id = "DataProviders",
                Title = "Data providers",
                Content = new Func<IServiceProvider, object>(_ => dataProvidersView),
                DockCapabilityOverrides = new()
            };

            DockableRegistry = new Dictionary<string, IDockable> {
                [torrentListing.Id] = torrentListing,
                [actionQueue.Id] = actionQueue,
                [log.Id] = log,
                [rightTool.Id] = rightTool,
            };

            // Default layout — replaced by RestoreStructure in MainWindow if saved state exists.
            var documentDock = new DocumentDock {
                Id = "Documents",
                VisibleDockables = DockFactory.CreateList<IDockable>(torrentListing, actionQueue, log),
                ActiveDockable = torrentListing,
                DockCapabilityPolicy = new(),
                DockCapabilityOverrides = new()
            };

            var mainLayout = new ProportionalDock {
                VisibleDockables = DockFactory.CreateList<IDockable>(
                    documentDock,
                    new ProportionalDockSplitter(),
                    new ToolDock {
                        Id = "RightPane",
                        Alignment = Alignment.Right,
                        Proportion = 0.1,
                        VisibleDockables = DockFactory.CreateList<IDockable>(rightTool),
                        ActiveDockable = rightTool,
                        DockCapabilityPolicy = new(),
                        DockCapabilityOverrides = new()
                    }),
                DockCapabilityPolicy = new()
            };

            RootDock = new RootDock { Id = "Root" };
            RootDock.VisibleDockables = DockFactory.CreateList<IDockable>(mainLayout);
            RootDock.DefaultDockable = mainLayout;

            DockFactory.HostWindowLocator = new() {
                [nameof(IDockWindow)] = () => new HostWindow()
            };
            // InitLayout is called by MainWindow after optionally restoring saved state.
        }

        public void PostStartup()
        {
            TorrentListingViewModel.AttachGridData();
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
        public void ShowStatisticsWindow()
        {
            var view = new StatisticsView {
                DataContext = new StatisticsViewModel()
            };
            DockUtils.SpawnToolWindow(view, "Statistics");
        }
    }
}
