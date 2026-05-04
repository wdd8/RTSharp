using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using Dock.Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;

using Nito.AsyncEx;

using RTSharp.Core;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Core.Services.Cache.TorrentPropertiesCache;
using RTSharp.Core.Services.Cache.TrackerDb;
using RTSharp.Core.TorrentPolling;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.Shared.Controls;
using RTSharp.Shared.Controls.Views;
using RTSharp.ViewModels;

using Serilog;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    private static readonly TimeSpan StartupContentResizeQuietPeriod = TimeSpan.FromMilliseconds(350);

    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainWindowViewModel DataContext)
    {
        ViewModel = DataContext;
        InitializeComponent();
        ShowInTaskbar = true;
        ViewModel!.ShowPluginsDialog = ShowPluginsDialogAsync;
        ViewModel!.ShowServersDialog = ShowServersDialogAsync;
        Opened += EvOpened;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();

        var x = config.UIState.Value.LastPosX;
        var y = config.UIState.Value.LastPosY;
        if (x != null && y != null) {
            this.Position = new Avalonia.PixelPoint(x.Value, y.Value);
            if (config.UIState.Value.Maximized)
                this.WindowState = WindowState.Maximized;
        }

        bool restored = false;
        if (!String.IsNullOrEmpty(config.UIState.Value.DockState)) {
            restored = Core.DockStateSerializer.Restore(
                ViewModel!.RootDock,
                ViewModel!.DockFactory,
                ViewModel!.DockableRegistry,
                config.UIState.Value.DockState);
        }
        if (!restored)
            ViewModel!.DockFactory.InitLayout(ViewModel!.RootDock);

        this.AddHandler(DragDrop.DropEvent, EvDragDrop);

        var fileMenu = (MenuItem)Resources["FileMenu"]!;
        var viewMenu = (MenuItem)Resources["ViewMenu"]!;
        var toolsMenu = (MenuItem)Resources["ToolsMenu"]!;
        var pluginsMenu = (MenuItem)Resources["PluginsMenu"]!;
        var serversMenu = (MenuItem)Resources["ServersMenu"]!;

        this.ViewModel!.MenuItems.Add(fileMenu);
        this.ViewModel!.MenuItems.Add(viewMenu);
        this.ViewModel!.MenuItems.Add(toolsMenu);
        this.ViewModel!.MenuItems.Add(pluginsMenu);
        this.ViewModel!.MenuItems.Add(serversMenu);

        App.RegisterOnExit($"{nameof(MainWindow)}_{nameof(SaveState)}", () => SaveState());
    }

    public async ValueTask SaveState()
    {
        string? serialized = null;
        AsyncManualResetEvent ev = new();

        Dispatcher.UIThread.Invoke(() => {
            serialized = Core.DockStateSerializer.Serialize(ViewModel!.RootDock);
            ev.Set();
        });

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();

        config.UIState.Value.LastPosX = this.Position.X;
        config.UIState.Value.LastPosY = this.Position.Y;
        config.UIState.Value.Maximized = this.WindowState == WindowState.Maximized;

        await ev.WaitAsync();
        config.UIState.Value.DockState = serialized;

        await config.Rewrite();
    }

    private async void EvOpened(object? sender, System.EventArgs e)
    {
        
    }

    private async Task ShowPluginsDialogAsync(PluginsViewModel Input)
    {
        var dialog = new PluginsView();
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
        var files = e.DataTransfer.TryGetFiles();
        var uri = e.DataTransfer.TryGetText();

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
