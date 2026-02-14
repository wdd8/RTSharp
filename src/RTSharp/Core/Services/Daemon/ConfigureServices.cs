using Avalonia.Controls;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using MsBox.Avalonia.Enums;

using MsBox.Avalonia;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using Avalonia.Threading;
using MsBox.Avalonia.Models;

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
                    "RT#",
                    "No client certificate exists for server daemon, do you wish to generate a new one?",
                    ButtonEnum.YesNo,
                    Icon.Question,
                    WindowStartupLocation.CenterScreen);

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
            var publicPem = File.ReadAllText(CertPath);
            var privatePem = File.ReadAllText(KeyPath);
            var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);

            void register<T>(string name, Config.Models.Server server)
                where T : class
            {
                services
                    .AddGrpcClient<T>(name, o => {
                        o.Address = server.GetUri();
                        o.ChannelOptionsActions.Add(opts => {
                            opts.MaxReceiveMessageSize = int.MaxValue;
                        });
                    })
                    .ConfigurePrimaryHttpMessageHandler((provider) => {
                        var handler = new SocketsHttpHandler();
                        handler.SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                            ClientCertificates = [ new X509Certificate2(x509.Export(X509ContentType.Pkcs12)) ],

                            RemoteCertificateValidationCallback = (httpRequestMessage, cert, certChain, policyErrors) => {
                                if (server.VerifyNative ?? true) {
                                    if (policyErrors != System.Net.Security.SslPolicyErrors.None) {
                                        Dispatcher.UIThread.InvokeAsync(async () => {
                                            var wnd = MessageBoxManager.GetMessageBoxStandard(
                                            "RT#",
                                            "Certificate " + cert.GetCertHashString() + " is untrusted (" + policyErrors + ")",
                                            ButtonEnum.Ok,
                                            Icon.Error,
                                            WindowStartupLocation.CenterScreen);

                                            await wnd.ShowWindowDialogAsync(App.MainWindow);
                                        }).GetAwaiter().GetResult();

                                        return false;
                                    }

                                    return true;
                                }

                                var thumbprint = server.TrustedThumbprint;

                                if (thumbprint.Equals(cert.GetCertHashString(HashAlgorithmName.SHA256), StringComparison.OrdinalIgnoreCase)) {
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
    @$"Do you trust the following server certificate? {(policyErrors == System.Net.Security.SslPolicyErrors.None ? "(trusted by system)" : "")}

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
                                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                                        });

                                        var res = await wnd.ShowWindowAsync();
                                        return res == "Yes";
                                    }).GetAwaiter().GetResult();

                                    if (result) {
                                        server.TrustedThumbprint = cert.GetCertHashString(HashAlgorithmName.SHA256);

                                        var config = provider.GetRequiredService<Config>();
                                        config.Rewrite().GetAwaiter().GetResult();
                                    } else {
                                    }

                                    return result;
                                }

                                return true;
                            }
                        };
                        return handler;
                    });
            }

            if (servers != null) {
                foreach (var server in servers) {
                    register<RTSharp.Daemon.Protocols.GRPCServerService.GRPCServerServiceClient>(nameof(RTSharp.Daemon.Protocols.GRPCServerService.GRPCServerServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.GRPCFilesService.GRPCFilesServiceClient>(nameof(RTSharp.Daemon.Protocols.GRPCFilesService.GRPCFilesServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentsService.GRPCTorrentsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentsService.GRPCTorrentsServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.GRPCStatsService.GRPCStatsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.GRPCStatsService.GRPCStatsServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCRtorrentSettingsService.GRPCRtorrentSettingsServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCQBittorrentSettingsService.GRPCQBittorrentSettingsServiceClient) + "_" + server.Key, server.Value);
                    register<RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient>(nameof(RTSharp.Daemon.Protocols.DataProvider.Settings.GRPCTransmissionSettingsService.GRPCTransmissionSettingsServiceClient) + "_" + server.Key, server.Value);
                }
            }
        }
    }
}
