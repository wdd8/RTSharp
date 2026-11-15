using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Core.Services.Daemon;

using Serilog;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Avalonia.Controls;
using RTSharp.Models;
using RTSharp.Views;

namespace RTSharp.ViewModels;

public partial class ManageServersWindowViewModel : ObservableObject
{
    public ObservableCollection<Server> ConfiguredServers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(BrowseFileSystemCommand))]
    public partial Server? SelectedServer { get; set; }

    public Func<ServerDialogViewModel, Task<bool>> ShowServerDialog { get; set; } = null!; // view set

    public ManageServersWindowViewModel()
    {
        Refresh();
    }

    private void Refresh()
    {
        var selectedId = SelectedServer?.ServerId;
        ConfiguredServers.Clear();

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        foreach (var (id, server) in config.Servers.Value.OrderBy(x => x.Key)) {
            ConfiguredServers.Add(new Server
            {
                ServerId = id,
                Host = server.Host,
                Port = server.DaemonPort,
            });
        }

        SelectedServer = ConfiguredServers.FirstOrDefault(x => x.ServerId == selectedId);
    }

    partial void OnSelectedServerChanged(Server? value)
    {
        if (value != null) {
            value.ConnectionStatus = "";
            value.Latency = null;
            value.CertThumbprint = null;
            value.CertNotBefore = null;
            value.CertNotAfter = null;
            value.CertAlgorithm = null;
            _ = TestConnection(value);
        }
    }

    [RelayCommand]
    public async Task Add()
    {
        var dialog = new ServerDialogViewModel {
            Server = new Server { ServerId = "", Host = "", Port = 12345, CertThumbprint = "", ConnectionStatus = "" }
        };

        if (await ShowServerDialog(dialog))
            await Save(dialog);
    }

    public bool CanEdit() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    public async Task Edit()
    {
        var server = SelectedServer!;

        string trustedThumbprint;
        using (var scope = Core.ServiceProvider.CreateScope())
            trustedThumbprint = scope.ServiceProvider.GetRequiredService<Core.Config>().Servers.Value[server.ServerId].TrustedThumbprint;

        var dialog = new ServerDialogViewModel {
            OriginalServerId = server.ServerId,
            Server = new Server {
                ServerId = server.ServerId,
                Host = server.Host,
                Port = server.Port,
                CertThumbprint = trustedThumbprint,
                // What the daemon presented the last time the server was tested
                CertNotBefore = server.CertNotBefore,
                CertNotAfter = server.CertNotAfter,
                CertAlgorithm = server.CertAlgorithm,
                ConnectionStatus = server.ConnectionStatus,
                Latency = server.Latency
            }
        };

        if (await ShowServerDialog(dialog))
            await Save(dialog);
    }

    private async Task Save(ServerDialogViewModel Dialog)
    {
        var ds = Dialog.Server;
        var serverId = ds.ServerId.Trim();

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var servers = config.Servers.Value;

        var serverConfig = new Core.Config.Models.Server {
            Host = ds.Host.Trim(),
            DaemonPort = ds.Port,
            TrustedThumbprint = ds.CertThumbprint!.Trim().ToUpperInvariant()
        };

        if (Dialog.OriginalServerId != null) {
            Core.Servers.Remove(Dialog.OriginalServerId);
            servers.Remove(Dialog.OriginalServerId);
        }

        Core.Servers.AddActionQueue(Core.Servers.Connect(serverId, serverConfig));
        servers[serverId] = serverConfig;

        await config.Rewrite();
        Refresh();
    }

    public bool CanDelete() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public async Task Delete()
    {
        var server = SelectedServer!;

        var box = MessageBoxManager.GetMessageBoxStandard(
            title: "RT# - Servers",
            text: $"Delete server \"{server.ServerId}\" ({server.Host}:{server.Port})?",
            @enum: ButtonEnum.YesNo,
            icon: Icon.Warning,
            windowStartupLocation: WindowStartupLocation.CenterOwner);

        if (await box.ShowAsync() != ButtonResult.Yes)
            return;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        Core.Servers.Remove(server.ServerId);
        config.Servers.Value.Remove(server.ServerId);

        await config.Rewrite();
        Refresh();
    }

    public bool CanBrowseFileSystem() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanBrowseFileSystem))]
    public async Task BrowseFileSystem()
    {
        if (!Core.Servers.All.TryGetValue(SelectedServer!.ServerId, out var daemonService)) {
            Log.Logger.Error("Server not found");
            return;
        }

        var wnd = new DirectorySelectorWindow {
            ViewModel = new DirectorySelectorWindowViewModel(daemonService) {
                WindowTitle = $"RT# - Browse File System ({SelectedServer.ServerId})",
                IsBrowser = true
            }
        };

        await wnd.ViewModel.SetCurrentFolder("/");
        wnd.Show(App.MainWindow);
    }

    public bool CanTestConnection() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanTestConnection))]
    public async Task TestConnection() => await TestConnection(SelectedServer!);

    private async Task TestConnection(Server server)
    {
        server.ConnectionStatus = "Testing...";
        server.Latency = null;

        if (!Core.Servers.All.TryGetValue(server.ServerId, out var daemonService)) {
            Log.Logger.Error("Server not found");
            return;
        }

        try {
            var sw = Stopwatch.StartNew();
            await daemonService.Ping(default);
            sw.Stop();

            server.ConnectionStatus = "OK";
            server.Latency = Shared.Utils.Converters.ToAgoString(sw.Elapsed);

            if (DaemonService.RemoteCerts.TryGetValue(server.ServerId, out var cert)) {
                server.CertThumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);
                server.CertNotBefore = cert.NotBefore.ToString("o");
                server.CertNotAfter = cert.NotAfter.ToString("o");
            }

            if (DaemonService.RemoteCipherSuites.TryGetValue(server.ServerId, out var cipherSuite))
                server.CertAlgorithm = cipherSuite;
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Ping to daemon has failed");
            while (ex.InnerException != null)
                ex = ex.InnerException;
            server.ConnectionStatus = ex.Message;
            server.Latency = null;
        }
    }
}
