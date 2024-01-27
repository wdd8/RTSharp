using System.Reactive;
using System.Threading.Tasks;
using RTSharp.Core.Services.Cache.ASCache;
using RTSharp.Core.Services.Cache.Images;
using RTSharp.Core.Services.Cache.TorrentFileCache;
using RTSharp.Shared.Controls.ViewModels;
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

namespace RTSharp.Views;

public partial class MainWindow : VmWindow<MainWindowViewModel>
{
	public MainWindow()
	{
		InitializeComponent();
	}

	public MainWindow(MainWindowViewModel DataContext)
	{
		InitializeComponent();

		ViewModel = DataContext;
		ShowInTaskbar = true;
		ViewModel!.ShowPluginsDialog = ShowPluginsDialogAsync;
        ViewModel!.ShowServersDialog = ShowServersDialogAsync;
        Opened += EvOpened;
		
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

        var waitingBox = new WaitingBox("Loading...", "Initializing and loading data providers and plugins...", WAITING_BOX_ICON.VISTA_WAIT);
		Log.Logger.Debug("Loading plugins...");

        _ = waitingBox.ShowDialog(this);
        await Plugin.Plugins.LoadPlugins(waitingBox);

		waitingBox.Report((33, "Loading caches..."));
		Log.Logger.Debug("Loading caches...");

		await cacheTasks;

        waitingBox.Report((66, "Starting..."));
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
		var files = e.Data.GetFileNames();
		var uri = e.Data.GetText();

		AddTorrentViewModel vm;
		var addTorrentWindow = new AddTorrentWindow() {
			DataContext = vm = new AddTorrentViewModel()
		};
		if (String.IsNullOrEmpty(uri) && files?.Any() == true) {
			vm.FromFileSelected = true;
			vm.SelectedFileTextBox = files.First();
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