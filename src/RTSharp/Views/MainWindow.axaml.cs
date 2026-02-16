using System.Threading.Tasks;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Shared.Controls.Views;
using RTSharp.ViewModels;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using RTSharp.Core.Services.Cache.TrackerDb;
using RTSharp.Core.TorrentPolling;
using Avalonia.Input;
using System.Linq;
using System;
using Avalonia.Controls;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using RTSharp.Shared.Controls;
using NP.Ava.UniDock;
using System.Collections.ObjectModel;
using RTSharp.ViewModels.TorrentListing;
using RTSharp.Core;
using NP.UniDockService;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel DataContext) : this()
    {
        ViewModel = DataContext;
        ShowInTaskbar = true;
        ViewModel!.ShowPluginsDialog = ShowPluginsDialogAsync;
        ViewModel!.ShowServersDialog = ShowServersDialogAsync;
        Opened += EvOpened;

        App.DockManager = (DockManager)Resources["MainDockManager"]!;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();
        App.DockManager.DockItemsViewModels = new ObservableCollection<DockItemViewModelBase>() {
            // EdgeDock
            new DockDataProvidersViewModel() {
                DockId = "DataProviders0",
                DefaultDockGroupId = "EdgeDock",
                Header = null,
                TheVM = new DataProvidersViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "DataProvidersTemplate",
                IsPredefined = false
            },

            // MainGroup
            new DockLogEntriesViewModel() {
                DockId = "LogEntries0",
                DefaultDockGroupId = "MainGroup",
                Header = null,
                TheVM = new LogEntriesViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "LogEntriesTemplate",
                IsPredefined = false
            },
            new DockActionQueuesViewModel() {
                DockId = "ActionQueue0",
                DefaultDockGroupId = "MainGroup",
                Header = null,
                TheVM = new ActionQueuesViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "ActionQueueTemplate",
                IsPredefined = false
            },
            new DockTorrentListingViewModel() {
                DockId = "TorrentListing0",
                DefaultDockGroupId = "MainGroup",
                Header = null,
                TheVM = new TorrentListingViewModel(),
                HeaderContentTemplateResourceKey = "TabHeaderTemplate",
                ContentTemplateResourceKey = "TorrentListingTemplate",
                IsPredefined = false,
                IsSelected = true
            }
        };
        if (!String.IsNullOrEmpty(config.UIState.Value.DockState) && !String.IsNullOrEmpty(config.UIState.Value.DockVMState)) {
            // Doesn't really work, too much things to hack around
            /*using (var mem = new MemoryStream()) {
                var utf8 = Encoding.UTF8.GetBytes(config.UIState.Value.DockVMState);
                mem.Write(utf8);
                mem.Position = 0;

                App.DockManager.RestoreViewModelsFromStream(mem, [
                    typeof(DockTorrentListingViewModel),
                    typeof(DockActionQueueViewModel),
                    typeof(DockLogEntriesViewModel),
                    typeof(DockDataProvidersViewModel)
                ]);
            }

            using (var mem = new MemoryStream()) {
                var utf8 = Encoding.UTF8.GetBytes(config.UIState.Value.DockState);
                mem.Write(utf8);
                mem.Position = 0;

                App.DockManager.RestoreDockManagerParamsFromStream(mem, true);
            }*/
        }

        this.AddHandler(DragDrop.DropEvent, EvDragDrop);

        var fileMenu = (MenuItem)Resources["FileMenu"]!;
        var editMenu = (MenuItem)Resources["EditMenu"]!;
        var viewMenu = (MenuItem)Resources["ViewMenu"]!;
        var toolsMenu = (MenuItem)Resources["ToolsMenu"]!;
        var pluginsMenu = (MenuItem)Resources["PluginsMenu"]!;
        var serversMenu = (MenuItem)Resources["ServersMenu"]!;

        this.ViewModel!.MenuItems.Add(fileMenu);
        this.ViewModel!.MenuItems.Add(editMenu);
        this.ViewModel!.MenuItems.Add(viewMenu);
        this.ViewModel!.MenuItems.Add(toolsMenu);
        this.ViewModel!.MenuItems.Add(pluginsMenu);
        this.ViewModel!.MenuItems.Add(serversMenu);

        App.RegisterOnExit($"{nameof(MainWindow)}_{nameof(DockManager)}_{nameof(SaveDockState)}", () => SaveDockState());
    }

    public async ValueTask SaveDockState()
    {
        // Doesn't really work, too much things to hack around
        /*string dockState, vmState;
        using (var mem = new MemoryStream()) {
            Dispatcher.UIThread.Invoke(() => {

                App.DockManager.SaveDockManagerParamsToStream(mem);
            });
            dockState = Encoding.UTF8.GetString(mem.ToArray());
        }

        using (var mem = new MemoryStream()) {
            Dispatcher.UIThread.Invoke(() => {
                App.DockManager.SaveViewModelsToStream(mem);
            });
            vmState = Encoding.UTF8.GetString(mem.ToArray());
        }

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();

        config.UIState.Value.DockState = dockState;
        config.UIState.Value.DockVMState = vmState;

        await config.Rewrite();*/
    }

    private async void EvOpened(object? sender, System.EventArgs e)
    {
        using var scope = Core.ServiceProvider.CreateScope();
        var cacheTasks = Task.WhenAll(
            scope.ServiceProvider.GetRequiredService<TorrentFileCache>().Initialize(),
            scope.ServiceProvider.GetRequiredService<TorrentPropertiesCache>().Initialize(),
            scope.ServiceProvider.GetRequiredService<ASCache>().Initialize(),
            scope.ServiceProvider.GetRequiredService<ImageCache>().Initialize(),
            scope.ServiceProvider.GetRequiredService<TrackerDb>().Initialize()
        );

        var domainParser = scope.ServiceProvider.GetRequiredService<Core.Services.DomainParser>();
        var domainParserTask = domainParser.Initialize();

        var waitingBox = new WaitingBox("Loading...", "Initializing and loading data providers and plugins...", BuiltInIcons.VISTA_WAIT);
        Log.Logger.Debug("Loading plugins...");

        _ = waitingBox.ShowDialog(this);
        const int TOTAL_TASKS = 4;
        int curProgress = 100 / TOTAL_TASKS;
        int progress() => curProgress += 100 / TOTAL_TASKS;

        await Plugin.Plugins.LoadPlugins(waitingBox);

        waitingBox.Report((progress(), "Loading caches..."));
        Log.Logger.Debug("Loading caches...");

        await cacheTasks;

        waitingBox.Report((progress(), "Loading domain parser..."));
        Log.Logger.Debug("Loading domain parser...");

        await domainParserTask;

        waitingBox.Report((progress(), "Starting..."));
        Log.Logger.Debug("Starting...");

        TorrentPolling.Start();

        waitingBox.Close();

        Log.Logger.Information("Ready");
    }

    private async Task ShowPluginsDialogAsync(PluginsViewModel Input)
    {
        var dialog = new Plugins();
        Input.ThisWindow = dialog;
        dialog.ViewModel = Input;

        await dialog.ShowDialog(this);
    }

    private async Task ShowServersDialogAsync(ServersWindowViewModel Input)
    {
        var dialog = new ServersWindow();
        dialog.ViewModel = Input;

        await dialog.ShowDialog(this);
    }

    private async void EvDragDrop(object sender, DragEventArgs e)
    {
        var files = e.Data.GetFiles();
        var uri = e.Data.GetText();

        AddTorrentViewModel vm;
        var addTorrentWindow = new AddTorrentWindow() {
            DataContext = vm = new AddTorrentViewModel()
        };
        if (String.IsNullOrEmpty(uri) && files?.Any() == true) {
            vm.FromFileSelected = true;
            vm.SelectedFileTextBox = files.First().Path.LocalPath;
        }
        if (files?.Any() != true && !String.IsNullOrEmpty(uri) && Uri.TryCreate(uri, UriKind.Absolute, out var _)) {
            vm.FromUriSelected = true;
            vm.Uri = uri;
        }

        foreach (var hook in Plugin.Plugins.GetHookAsync<object>(Plugin.Plugins.HookType.AddTorrent_EvDragDrop)) {
            try {
                await hook(vm);
            } catch (Exception ex) {
                Log.Logger.Error(ex, "EvDragDrop hook error");
            }
        }

        addTorrentWindow.Show();
    }
}