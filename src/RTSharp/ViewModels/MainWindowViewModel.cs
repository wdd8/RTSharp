using Avalonia.Controls;
using Avalonia.Dialogs;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Avalonia.Controls;
using Dock.Model.Core;

using Microsoft.Extensions.DependencyInjection;

using RTSharp.Core;
using RTSharp.Shared.Controls;
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

namespace RTSharp.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly TorrentListingViewModel TorrentListingViewModel;

    public ObservableCollection<MenuItem> MenuItems { get; } = new();

    public ObservableCollection<Plugin.RTSharpPlugin> Plugins => Plugin.Plugins.LoadedPlugins;

    public Func<Task> ShowPluginsDialog { get; set; } = null!; // view set

    [ObservableProperty]
    public partial string StringFilter { get; set; } = "";

    [ObservableProperty]
    public partial string Title { get; set; } = "RT#";

    public IFactory DockFactory { get; }

    public RootDock RootDock { get; }

    /// <summary>
    /// <see cref="RootDock"/>, once the window is shown. The layout and torrent grid in it take longer to build than the rest of the window
    /// </summary>
    [ObservableProperty]
    public partial RootDock? Layout { get; set; }

    public IReadOnlyDictionary<string, IDockable> DockableRegistry { get; }

    public MainWindowViewModel(DataGridPersistedState? TorrentGridState)
    {
        ((AppViewModel)Avalonia.Application.Current!.DataContext!).PropertyChanged += (sender, e) => {
            Title = ((AppViewModel)sender!).TrayIconText;
        };

        DockFactory = new RTSharpDockFactory();

        TorrentListingViewModel = new TorrentListingViewModel();
        var actionQueuesViewModel = new ActionQueuesViewModel();
        var logEntriesViewModel = new LogEntriesViewModel();
        var dataProvidersViewModel = new DataProvidersViewModel();

        var startupGridState = TorrentGridState;

        TorrentListingView CreateTorrentListingView()
        {
            var view = new TorrentListingView { DataContext = TorrentListingViewModel };

            // A view built again, e.g. when the listing is moved, takes the state the previous one saved
            var gridState = startupGridState;
            startupGridState = null;
            if (gridState == null) {
                using var viewScope = Core.ServiceProvider.CreateScope();
                gridState = TorrentListingView.ParseGridState(viewScope.ServiceProvider.GetRequiredService<Core.Config>().UIState.Value.TorrentGridState);
            }

            if (gridState != null)
                view.RestoreGridState(gridState);
            return view;
        }

        var torrentListing = new DocumentWithIcon {
            Id = "TorrentListing",
            Title = "Torrent Listing",
            Icon = BuiltInIcons.TORRENT_LISTING,
            // Built in the same pass as the dock layout, rather than in a later background priority pass
            DeferContentPresentation = false,
            Content = new Func<IServiceProvider, object>(_ => CreateTorrentListingView()),
            DockCapabilityOverrides = new()
        };

        var actionQueue = new DocumentWithIcon {
            Id = "ActionQueue",
            Title = "Action Queue",
            Icon = BuiltInIcons.ACTION_QUEUE,
            Content = new Func<IServiceProvider, object>(_ => new ActionQueuesView {
                DataContext = actionQueuesViewModel
            }),
            DockCapabilityOverrides = new()
        };

        var log = new DocumentWithIcon {
            Id = "Log",
            Title = "Log",
            Icon = BuiltInIcons.LOG,
            Content = new Func<IServiceProvider, object>(_ => new LogEntriesView {
                DataContext = logEntriesViewModel
            }),
            DockCapabilityOverrides = new()
        };

        var rightTool = new Tool {
            Id = "DataProviders",
            Title = "Data Providers",
            Content = new Func<IServiceProvider, object>(_ => new DataProvidersView {
                DataContext = dataProvidersViewModel
            }),
            DockCapabilityOverrides = new()
        };

        DockableRegistry = new Dictionary<string, IDockable> {
            [torrentListing.Id] = torrentListing,
            [actionQueue.Id] = actionQueue,
            [log.Id] = log,
            [rightTool.Id] = rightTool,
        };

        var documentDock = new ImmediateDocumentDock {
            Id = "Documents",
            VisibleDockables = DockFactory.CreateList<IDockable>(torrentListing, actionQueue, log),
            ActiveDockable = torrentListing,
            DockCapabilityPolicy = new(),
            DockCapabilityOverrides = new()
        };

        var mainLayout = new ImmediateProportionalDock {
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

        RootDock = new ImmediateRootDock { Id = "Root" };
        RootDock.VisibleDockables = DockFactory.CreateList<IDockable>(mainLayout);
        RootDock.DefaultDockable = mainLayout;

        DockFactory.HostWindowLocator = new() {
            [nameof(IDockWindow)] = () => new HostWindow()
        };
        // InitLayout is called by MainWindow after optionally restoring saved state.
    }

    public void PostStartup()
    {
        Layout = RootDock;
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
            ViewModel = new()
        };
        optionsWindow.Show(App.MainWindow);
    }

    [RelayCommand]
    public void AboutClick()
    {
        var aboutWindow = new AboutWindow() {
            ViewModel = new()
        };
        aboutWindow.Show(App.MainWindow);
    }

    [RelayCommand]
    public void AddTorrentClick()
    {
        var addTorrentWindow = new AddTorrentWindow() {
            ViewModel = new()
        };
        addTorrentWindow.Show(App.MainWindow);
    }

    [RelayCommand]
    public void PluginsClick()
    {
        ShowPluginsDialog();
    }

    [RelayCommand]
    public void ServersClick()
    {
        var wnd = new ManageServersWindow {
            ViewModel = new()
        };
        wnd.Show(App.MainWindow);
    }

    [RelayCommand]
    public void TorrentCreatorClick()
    {
        var torrentCreatorWindow = new TorrentCreatorWindow() {
            ViewModel = new()
        };
        torrentCreatorWindow.Show(App.MainWindow);
    }

    [RelayCommand]
    public void ShowStatisticsWindow()
    {
        var view = new StatisticsView {
            DataContext = new StatisticsViewModel()
        };
        DockUtils.SpawnToolWindow(view);
    }
}

/*
 * Dock builds each level of a layout in a background priority pass of its own, and Avalonia holds those back while the
 * window has input pending, which it does as it first shows. These levels are built in the layout pass of their parent
 */
public class ImmediateRootDock : RootDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}

public class ImmediateProportionalDock : ProportionalDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}

public class ImmediateDocumentDock : DocumentDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}
