using Avalonia.Controls;
using Avalonia.Threading;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Daemon
{
    public static class ConfigureServices
    {
        private static (string Public, string Private) GenerateCert()
        {
            using ECDsa ecdsa = ECDsa.Create();
            ecdsa.KeySize = 256;

            var request = new CertificateRequest(new X500DistinguishedName($"CN={Environment.UserName}"), ecdsa, HashAlgorithmName.SHA256);

            var x509 = request.CreateSelfSigned(
                DateTimeOffset.Now,
                DateTimeOffset.Now.AddDays(36500));

            var exportData = x509.Export(X509ContentType.Cert);
            var publicKey = new string(PemEncoding.Write("CERTIFICATE", exportData));
            var privateKey = ecdsa.ExportECPrivateKeyPem();

            return (publicKey, privateKey);
        }

        static readonly string CertPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client.pem");
        static readonly string KeyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "client.key");

        public static async Task GenerateCertificatesIfNeeded()
        {
            if (!Path.Exists(CertPath) || !Path.Exists(KeyPath)) {
                var res = await Dispatcher.UIThread.InvokeAsync(async () => {
                    var wnd = MessageBoxManager.GetMessageBoxStandard(
                    title: "RT#",
                    text: "No client certificate exists for server daemon, do you wish to generate a new one?",
                    @enum: ButtonEnum.YesNo,
                    icon: Icon.Question,
                    windowStartupLocation: WindowStartupLocation.CenterScreen);

                    return await wnd.ShowWindowAsync();
                });
                if (res == ButtonResult.Yes) {
                    var (publicKey, privateKey) = GenerateCert();

                    await File.WriteAllTextAsync(CertPath, publicKey);
                    await File.WriteAllTextAsync(KeyPath, privateKey);
                } else {
                    throw new InvalidOperationException("Cannot authenticate with server without certificate");
                }
            }
        }

        public static void AddDaemonServices(this IServiceCollection services, Dictionary<string, Config.Models.Server> servers)
        {
            var x509 = X509Certificate2.CreateFromPemFile(CertPath, KeyPath);
            var pkcs12 = X509CertificateLoader.LoadPkcs12(x509.Export(X509ContentType.Pkcs12), null);

            void register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(string name, string serverId, Config.Models.Server server)
                where T : class
            {
                services
                    .AddGrpcClient<T>(name + "_" + serverId, o => {
                        o.Address = server.GetUri();
                        o.ChannelOptionsActions.Add(opts => {
                            opts.MaxReceiveMessageSize = int.MaxValue;
                        });
                    })
                    .ConfigurePrimaryHttpMessageHandler((provider) => {
                        var handler = new SocketsHttpHandler();
                        handler.PlaintextStreamFilter = (context, cancellationToken) => {
                            if (context.PlaintextStream is System.Net.Security.SslStream tlsStm)
                                DaemonService.RemoteCipherSuites[serverId] = tlsStm.NegotiatedCipherSuite.ToString();

                            return ValueTask.FromResult(context.PlaintextStream);
                        };

                        handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                            ClientCertificates = [ pkcs12 ],

                            RemoteCertificateValidationCallback = (httpRequestMessage, cert, certChain, policyErrors) => {
                                if (cert == null)
                                    return false;

                                var thumbprint = server.TrustedThumbprint;

                                if (thumbprint?.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase) != true) {
                                    var result = Dispatcher.UIThread.InvokeAsync(async () => {
                                        var wnd = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                                            Icon = Icon.Warning,
                                            ContentTitle = "RT#",
                                            ContentMessage = (!String.IsNullOrEmpty(thumbprint) ?
    @"```
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
@    WARNING: REMOTE HOST IDENTIFICATION HAS CHANGED!     @
@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@@
**IT IS POSSIBLE THAT SOMEONE IS DOING SOMETHING NASTY!**
Someone could be eavesdropping on you right now (man-in-the-middle attack)!
It is also possible that a host key has just been changed.
```

" : "") +
    @$"Do you trust the following server certificate?

```
{cert.ToString(true)}
```",
                                            Markdown = true,
                                            ButtonDefinitions = new[] {
                                            new ButtonDefinition() {
                                                IsDefault = false,
                                                Name = "Yes"
                                            },
                                            new ButtonDefinition() {
                                                IsDefault = true,
                                                Name = "No"
                                            },
                                        },
                                            WindowStartupLocation = WindowStartupLocation.CenterOwner,
                                            MaxWidth = 700,
                                            Width = 700
                                        });

                                        var res = await wnd.ShowWindowAsync();
                                        return res == "Yes";
                                    }).GetAwaiter().GetResult();

                                    if (result) {
                                        server.TrustedThumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);

                                        var config = provider.GetRequiredService<Config>();
                                        config.Rewrite().GetAwaiter().GetResult();
                                    }

                                    if (result && cert is X509Certificate2 acceptedCert)
                                        DaemonService.RemoteCerts[serverId] = acceptedCert;

                                    return result;
                                }

                                if (cert is X509Certificate2 trustedCert)
                                    DaemonService.RemoteCerts[serverId] = trustedCert;

                                return true;
                            }
                        };
                        return handler;
                    });
            }

            if (servers != null) {
                foreach (var server in servers) {
                    register<RTSharp.Daemon.Protocols.GRPCServerService.GRPCServerServiceClient>(nameof(RTSharp.Daemon.Protocols.GRPCServerService.GRPCServerServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.GRPCFilesService.GRPCFilesServiceClient>(nameof(RTSharp.Daemon.Protocols.GRPCFilesService.GRPCFilesServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentsService.GRPCTorrentsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentsService.GRPCTorrentsServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCStatsService.GRPCStatsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCStatsService.GRPCStatsServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient), server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient), server.Key, server.Value);
                }
            }
        }
    }
}
