using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Google.Protobuf.WellKnownTypes;

using Grpc.Net.Client;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Core.Services.Daemon;
using RTSharp.Daemon.Protocols;

using Serilog;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

using Avalonia.Controls;
using RTSharp.Models;

namespace RTSharp.ViewModels;

public partial class ManageServersWindowViewModel : ObservableObject
{
    public ObservableCollection<Server> ConfiguredServers { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(BeginEditCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    [NotifyCanExecuteChangedFor(nameof(TestConnectionCommand))]
    public partial Server? SelectedServer { get; set; }

    [ObservableProperty]
    public partial bool IsDialogOpen { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DialogTitle))]
    [NotifyPropertyChangedFor(nameof(DialogConfirmText))]
    public partial bool IsDialogForNew { get; set; }

    [ObservableProperty]
    public partial Server? DialogServer { get; set; }

    public string DialogTitle => IsDialogForNew ? "Add server" : "Edit server";
    public string DialogConfirmText => IsDialogForNew ? "Add" : "Save";

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
    public void BeginAdd()
    {
        IsDialogForNew = true;
        DialogServer = new Server { ServerId = "", Host = "", Port = 12345, CertThumbprint = "", ConnectionStatus = "" };
        IsDialogOpen = true;
    }

    public bool CanBeginEdit() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanBeginEdit))]
    public void BeginEdit()
    {
        var server = SelectedServer!;
        IsDialogForNew = false;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var serverConfig = config.Servers.Value[server.ServerId];

        DialogServer = new Server {
            ServerId = server.ServerId,
            Host = server.Host,
            Port = server.Port,
            CertThumbprint = serverConfig.TrustedThumbprint,
            ConnectionStatus = ""
        };
        IsDialogOpen = true;
    }

    [RelayCommand]
    public async Task ConfirmDialog()
    {
        var ds = DialogServer!;
        var serverId = ds.ServerId.Trim();

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();
        var servers = config.Servers.Value;

        if (IsDialogForNew && servers.ContainsKey(serverId)) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Manage Servers",
                $"A server with ID \"{serverId}\" already exists.",
                ButtonEnum.Ok, Icon.Stop).ShowWindowAsync();
            return;
        }

        var oldServerId = !IsDialogForNew ? SelectedServer!.ServerId : null;

        if (oldServerId != null && oldServerId != serverId && servers.ContainsKey(serverId)) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Manage Servers",
                $"A server with ID \"{serverId}\" already exists.",
                ButtonEnum.Ok, Icon.Stop).ShowWindowAsync();
            return;
        }

        var serverConfig = new Core.Config.Models.Server {
            Host = ds.Host.Trim(),
            DaemonPort = ds.Port,
            TrustedThumbprint = ds.CertThumbprint!.Trim().ToUpperInvariant()
        };

        if (oldServerId != null) {
            Core.Servers.Remove(oldServerId);
            servers.Remove(oldServerId);
        }

        Core.Servers.Add(serverId, serverConfig);
        servers[serverId] = serverConfig;

        IsDialogOpen = false;
        await config.Rewrite();
        Refresh();
    }

    [RelayCommand]
    public async Task TestDialogConnection()
    {
        var ds = DialogServer!;
        if (String.IsNullOrWhiteSpace(ds.Host) || String.IsNullOrWhiteSpace(ds.CertThumbprint))
            return;

        ds.ConnectionStatus = "Testing...";
        ds.Latency = null;

        var temp = new Core.Config.Models.Server {
            Host = ds.Host.Trim(),
            DaemonPort = ds.Port,
            TrustedThumbprint = ds.CertThumbprint.Trim().ToUpperInvariant()
        };

        GrpcChannel? channel = null;
        try {
            channel = ConfigureServices.CreateChannel("__dialog_test__", temp, cert => temp.TrustedThumbprint.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase));
            var client = new GRPCServerService.GRPCServerServiceClient(channel);

            var sw = Stopwatch.StartNew();
            await client.TestAsync(new Empty());
            sw.Stop();

            ds.ConnectionStatus = "OK";
            ds.Latency = Shared.Utils.Converters.ToAgoString(sw.Elapsed);
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Test ping failed");
            while (ex.InnerException != null)
                ex = ex.InnerException;
            ds.ConnectionStatus = ex.Message;
            ds.Latency = null;
        } finally {
            channel?.Dispose();
        }
    }

    [RelayCommand]
    public void CancelDialog() => IsDialogOpen = false;

    public bool CanDelete() => SelectedServer != null;

    [RelayCommand(CanExecute = nameof(CanDelete))]
    public async Task Delete()
    {
        var server = SelectedServer!;
        IsDialogOpen = false;

        var box = MessageBoxManager.GetMessageBoxStandard(
            title: "RT# - Manage Servers",
            text: $"Delete server \"{server.ServerId}\" ({server.Host}:{server.Port})?",
            @enum: ButtonEnum.YesNo,
            icon: Icon.Warning,
            windowStartupLocation: WindowStartupLocation.CenterScreen);

        if (await box.ShowAsync() != ButtonResult.Yes)
            return;

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        Core.Servers.Remove(server.ServerId);
        config.Servers.Value.Remove(server.ServerId);

        await config.Rewrite();
        Refresh();
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
