using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;

using RTSharp.Core;
using RTSharp.Shared.Abstractions.Client;
using RTSharp.ViewModels;

using Serilog;

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using File = System.IO.File;

namespace RTSharp.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
    private static readonly string HelpFile = Path.Combine(AppContext.BaseDirectory, "RTSharp.chm");

    private Avalonia.PixelPoint RestorePosition;
    private Avalonia.Size? RestoreSize;

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

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();

        var x = config.UIState.Value.LastPosX;
        var y = config.UIState.Value.LastPosY;
        var w = config.UIState.Value.LastWidth;
        var h = config.UIState.Value.LastHeight;

        if (x != null && y != null) {
            this.Position = new Avalonia.PixelPoint(x.Value, y.Value);
        }
        if (w != null && h != null) {
            this.Width = w.Value;
            this.Height = h.Value;
        }
        if (config.UIState.Value.Maximized)
            this.WindowState = WindowState.Maximized;

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

        this.Closing += (_, _) => {
            RestorePosition = this.Position;

            if (this.WindowState == WindowState.Normal)
                RestoreSize = this.ClientSize;
            else
                RestoreSize = null;
        };

        this.AddHandler(DragDrop.DropEvent, EvDragDrop);

        var fileMenu = (MenuItem)Resources["FileMenu"]!;
        var viewMenu = (MenuItem)Resources["ViewMenu"]!;
        var toolsMenu = (MenuItem)Resources["ToolsMenu"]!;
        var pluginsMenu = (MenuItem)Resources["PluginsMenu"]!;
        var serversMenu = (MenuItem)Resources["ServersMenu"]!;
        var helpMenu = (MenuItem)Resources["HelpMenu"]!;

        this.ViewModel!.MenuItems.Add(fileMenu);
        this.ViewModel!.MenuItems.Add(viewMenu);
        this.ViewModel!.MenuItems.Add(toolsMenu);
        this.ViewModel!.MenuItems.Add(pluginsMenu);
        this.ViewModel!.MenuItems.Add(serversMenu);
        this.ViewModel!.MenuItems.Add(helpMenu);

        App.RegisterOnExit($"{nameof(MainWindow)}_{nameof(SaveState)}", SaveState);
    }

    public async ValueTask SaveState()
    {
        string? serialized = null;
        Avalonia.PixelPoint pos = default;
        Avalonia.Size? size = default;
        bool maximized = false;

        Dispatcher.UIThread.Invoke(() => {
            serialized = Core.DockStateSerializer.Serialize(ViewModel!.RootDock);
            pos = RestorePosition;
            size = RestoreSize;
            maximized = this.WindowState == WindowState.Maximized;
        });

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();

        config.UIState.Value.LastPosX = pos.X;
        config.UIState.Value.LastPosY = pos.Y;
        if (size != null) {
            config.UIState.Value.LastWidth = (int)size.Value.Width;
            config.UIState.Value.LastHeight = (int)size.Value.Height;
        }
        config.UIState.Value.Maximized = maximized;
        config.UIState.Value.DockState = serialized;

        await config.Rewrite();
    }

    [RelayCommand]
    private async Task OpenHelp(string? Topic)
    {
        if (!File.Exists(HelpFile)) {
            var msgbox = MessageBoxManager.GetMessageBoxStandard(
                title: "RTSharp",
                text: $"Help file not found at {HelpFile}",
                @enum: MsBox.Avalonia.Enums.ButtonEnum.Ok,
                icon: MsBox.Avalonia.Enums.Icon.Error);
            await msgbox.ShowAsync();
            return;
        }

        try {
            if (OperatingSystem.IsWindows()) {
                // Extracting a downloaded release zip tags the .chm with Mark of the Web, which makes hh.exe refuse to render any page
                try {
                    File.Delete(HelpFile + ":Zone.Identifier");
                } catch (Exception ex) {
                    Log.Logger.Warning(ex, "Failed to remove Mark of the Web from {HelpFile}", HelpFile);
                }

                var psi = new ProcessStartInfo("hh.exe");
                psi.ArgumentList.Add(Topic == null ? HelpFile : $"{HelpFile}::/{Topic}");
                Process.Start(psi)?.Dispose();
            } else {
                // No CHM viewer ships with Linux/macOS, defer to whatever the user has associated (xchm, kchmviewer, ...)
                Process.Start(new ProcessStartInfo(HelpFile) {
                    UseShellExecute = true
                })?.Dispose();
            }
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to open help file {HelpFile}", HelpFile);
        }
    }

    private async Task ShowPluginsDialogAsync()
    {
        var dialog = new PluginsView();
        var vm = new PluginsViewModel {
            ThisWindow = dialog
        };
        dialog.ViewModel = vm;

        await dialog.ShowDialog(this);
    }

    private async Task EvDragDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles()?.Where(x => !x.Path.OriginalString.StartsWith("virtual-file://"));
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
