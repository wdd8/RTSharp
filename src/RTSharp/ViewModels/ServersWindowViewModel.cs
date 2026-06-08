using Avalonia.Media.Imaging;

using CommunityToolkit.Mvvm.ComponentModel;

using RTSharp.Core.Services.Daemon;
using RTSharp.Shared.Controls;

using Serilog;

using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading.Tasks;

namespace RTSharp.ViewModels
{
    public partial class ServersWindowViewModel : ObservableObject
    {
        public Bitmap ServerIcon { get; } = BuiltInIcon.Get(BuiltInIcons.SERVER);
        public Bitmap CertificateIcon { get; } = BuiltInIcon.Get(BuiltInIcons.CERTIFICATE);

        [ObservableProperty]
        public partial ObservableCollection<Models.Server> Servers { get; set; }

        [ObservableProperty]
        public partial Models.Server? SelectedServer { get; set; }

        public ServersWindowViewModel()
        {
            Servers = [];

            foreach (var (id, server) in Core.Servers.Value) {
                Servers.Add(new Models.Server {
                    ServerId = id,
                    Host = server.Host,
                    Port = server.Port
                });
            }
        }

        partial void OnSelectedServerChanged(Models.Server? value)
        {
            if (value != null)
                _ = TestConnectionAsync(value);
        }

        private async Task TestConnectionAsync(Models.Server server)
        {
            server.ConnectionStatus = "Testing...";
            server.Latency = null;
            server.CertThumbprint = null;
            server.CertNotBefore = null;
            server.CertNotAfter = null;
            server.CertAlgorithm = null;

            try {
                var sw = Stopwatch.StartNew();
                await Core.Servers.Value[server.ServerId].Ping(default);
                sw.Stop();

                server.ConnectionStatus = "OK";
                server.Latency = Shared.Utils.Converters.ToAgoString(sw.Elapsed);

                if (DaemonService.RemoteCerts.TryGetValue(server.ServerId, out var cert)) {
                    server.CertThumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);
                    server.CertNotBefore = cert.NotBefore.ToString("o");
                    server.CertNotAfter = cert.NotAfter.ToString("o");
                }

                if (DaemonService.RemoteCipherSuites.TryGetValue(server.ServerId, out var cipherSuite))
                    server.CertAlgorithm = cipherSuite;
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Ping to daemon has failed");
                server.Latency = "?";

                while (ex.InnerException != null)
                    ex = ex.InnerException;

                server.ConnectionStatus = ex.Message.ToString();
            }
        }
    }
}
