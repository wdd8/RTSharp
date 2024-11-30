using RTSharp.DataProvider.Rtorrent.Protocols;

using System.Net.Http;
using System.Threading;
using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RTSharp.Shared.Abstractions;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using Avalonia.Threading;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;
using MsBox.Avalonia;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;

namespace RTSharp.DataProvider.Rtorrent.Plugin.Server
{
    public static class Clients
    {
        private static ServiceProvider Provider;

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

        private static List<string> UntrustedServers = new();

        public static async Task Initialize(IPluginHost Host)
        {
            var pluginConfig = Host.PluginConfig;

            var uri = pluginConfig.GetServerUri();

            var certPath = Path.Combine(Path.GetDirectoryName(Host.FullModulePath), "client.pem");
            var keyPath = Path.Combine(Path.GetDirectoryName(Host.FullModulePath), "client.key");

            if (!Path.Exists(certPath) || !Path.Exists(keyPath)) {
                var wnd = MessageBoxManager.GetMessageBoxStandard(
                    "rtorrent - " + Host.PluginInstanceConfig.Name + " (" + Host.InstanceId + ")",
                    "No client certificate exists, do you wish to generate a new one?",
                    ButtonEnum.YesNo,
                    Icon.Question,
                    Avalonia.Controls.WindowStartupLocation.CenterOwner);

                var res = await wnd.ShowWindowDialogAsync((Window)Host.MainWindow);
                if (res == ButtonResult.Yes) {
                    var (publicKey, privateKey) = GenerateCert();

                    await System.IO.File.WriteAllTextAsync(certPath, publicKey);
                    await System.IO.File.WriteAllTextAsync(keyPath, privateKey);
                } else {
                    throw new InvalidOperationException("Cannot authenticate with server without certificate");
                }
            }

            var publicPem = await System.IO.File.ReadAllTextAsync(certPath);
            var privatePem = await System.IO.File.ReadAllTextAsync(keyPath);
            var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory, LoggerFactory>();

            void register<T>()
                where T : class
            {
                services
                    .AddGrpcClient<T>(o => {
                        o.Address = new Uri(uri);
                        o.ChannelOptionsActions.Add(opts => {
                            opts.MaxReceiveMessageSize = int.MaxValue;
                        });
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => {
                        var handler = new HttpClientHandler();
                        handler.ClientCertificateOptions = ClientCertificateOption.Manual;
                        handler.ClientCertificates.Add(new X509Certificate2(x509.Export(X509ContentType.Pkcs12)));
                        handler.ServerCertificateCustomValidationCallback = (httpRequestMessage, cert, certChain, policyErrors) => {
                            if (UntrustedServers.Contains(cert.GetCertHashString()))
                                return false;

                            if (pluginConfig.VerifyNative()) {
                                if (policyErrors != System.Net.Security.SslPolicyErrors.None) {
                                    UntrustedServers.Add(cert.GetCertHashString());

                                    var mre = new ManualResetEventSlim(false);

                                    Dispatcher.UIThread.Post(async () => {
                                        var wnd = MessageBoxManager.GetMessageBoxStandard(
                                        "rtorrent - " + Host.PluginInstanceConfig.Name + " (" + Host.InstanceId + ")",
                                        "Certificate " + cert.Thumbprint + " is untrusted (" + policyErrors + ")",
                                        ButtonEnum.Ok,
                                        Icon.Error,
                                        WindowStartupLocation.CenterScreen);

                                        await wnd.ShowWindowDialogAsync((Window)Host.MainWindow);
                                        mre.Set();
                                    });

                                    mre.Wait();
                                    return false;
                                }

                                return true;
                            }

                            var thumbprint = pluginConfig.GetTrustedServerThumbprint();

                            if (thumbprint != cert.GetCertHashString()) {
                                var mre = new ManualResetEventSlim(false);
                                bool result = false;

                                Dispatcher.UIThread.Post(async () => {
                                    var wnd = MessageBoxManager.GetMessageBoxCustom(new MsBox.Avalonia.Dto.MessageBoxCustomParams {
                                        Icon = Icon.Warning,
                                        ContentTitle = "rtorrent - " + Host.PluginInstanceConfig.Name + " (" + Host.InstanceId + ")",
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
                                    mre.Set();
                                    result = res == "Yes";
                                });

                                mre.Wait();

                                if (result) {
                                    var el = JsonSerializer.Deserialize<JsonObject>(System.IO.File.ReadAllText(Host.PluginConfigPath));
                                    el["Server"]["TrustedThumbprint"] = cert.GetCertHashString();
                                    var outp = el.ToJsonString(new JsonSerializerOptions() {
                                        WriteIndented = true
                                    });
                                    System.IO.File.WriteAllText(Host.PluginConfigPath, outp);
                                } else
                                    UntrustedServers.Add(cert.GetCertHashString());

                                return result;
                            }

                            return true;
                        };
                        return handler;
                    });
            }

            register<GRPCTorrentsService.GRPCTorrentsServiceClient>();
            register<GRPCTorrentService.GRPCTorrentServiceClient>();

            Provider = services.BuildServiceProvider();
        }

        public static GRPCTorrentsService.GRPCTorrentsServiceClient Torrents() => Provider.GetRequiredService<GRPCTorrentsService.GRPCTorrentsServiceClient>();

        public static GRPCTorrentService.GRPCTorrentServiceClient Torrent() => Provider.GetRequiredService<GRPCTorrentService.GRPCTorrentServiceClient>();
    }
}
