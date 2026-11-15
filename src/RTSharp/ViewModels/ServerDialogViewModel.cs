using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading.Tasks;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

using Google.Protobuf.WellKnownTypes;

using Grpc.Net.Client;

using Microsoft.Extensions.DependencyInjection;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Core.Services.Daemon;
using RTSharp.Daemon.Protocols;
using RTSharp.Models;

using Serilog;

namespace RTSharp.ViewModels;

/// <summary>
/// Add Server and Edit Server dialog
/// </summary>
public partial class ServerDialogViewModel : ObservableObject
{
    public required Server Server { get; init; }

    public string? OriginalServerId { get; init; }

    public bool IsNew => OriginalServerId == null;

    public string Title => IsNew ? "RT# - Add Server" : "RT# - Edit Server";

    public string ConfirmText => IsNew ? "Add" : "OK";

    public bool HasCertificateDetails => !IsNew && Server.CertNotAfter != null;

    public Action<bool> CloseWithResult { get; set; } = null!; // view set

    [RelayCommand]
    public async Task Confirm()
    {
        if (!Server.IsServerValid) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Servers",
                "Enter a server ID, host and certificate thumbprint.",
                ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
            return;
        }

        var serverId = Server.ServerId.Trim();

        using var scope = Core.ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

        if (serverId != OriginalServerId && config.Servers.Value.ContainsKey(serverId)) {
            await MessageBoxManager.GetMessageBoxStandard("RT# - Servers",
                $"A server with ID \"{serverId}\" already exists. Enter a different server ID.",
                ButtonEnum.Ok, Icon.Error).ShowWindowAsync();
            return;
        }

        CloseWithResult(true);
    }

    [RelayCommand]
    public void Cancel() => CloseWithResult(false);

    [RelayCommand]
    public async Task Test()
    {
        if (String.IsNullOrWhiteSpace(Server.Host) || String.IsNullOrWhiteSpace(Server.CertThumbprint))
            return;

        Server.ConnectionStatus = "Testing...";
        Server.Latency = null;

        var temp = new Core.Config.Models.Server {
            Host = Server.Host.Trim(),
            DaemonPort = Server.Port,
            TrustedThumbprint = Server.CertThumbprint.Trim().ToUpperInvariant()
        };

        GrpcChannel? channel = null;
        try {
            channel = ConfigureServices.CreateChannel("__dialog_test__", temp, cert => temp.TrustedThumbprint.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase));
            var client = new GRPCServerService.GRPCServerServiceClient(channel);

            var sw = Stopwatch.StartNew();
            await client.TestAsync(new Empty());
            sw.Stop();

            Server.ConnectionStatus = "OK";
            Server.Latency = Shared.Utils.Converters.ToAgoString(sw.Elapsed);
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Test ping failed");
            while (ex.InnerException != null)
                ex = ex.InnerException;
            Server.ConnectionStatus = ex.Message;
            Server.Latency = null;
        } finally {
            channel?.Dispose();
        }
    }
}
