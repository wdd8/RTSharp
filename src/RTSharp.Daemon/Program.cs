using Microsoft.AspNetCore.Server.Kestrel.Core;

using RTSharp.Daemon.Services;
using RTSharp.Daemon.Utils;
using RTSharp.Shared.Abstractions;

using System.Net;
using System.Runtime.InteropServices;

public static class Program
{
    public static ReloadableServerCertificate ServerCertificate = null!; // set later

    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddLogging(options =>
        {
            options.AddSimpleConsole(c =>
            {
                c.TimestampFormat = "[yyyy-MM-ddTHH:mm:ss.fffffffK] ";
                c.SingleLine = true;
                c.ColorBehavior = Microsoft.Extensions.Logging.Console.LoggerColorBehavior.Default;
                c.IncludeScopes = false;
                c.UseUtcTimestamp = false;
            });
        });

        builder.Services.AddGrpc();
        builder.Services.AddSingleton<RTSharp.Shared.Abstractions.ISpeedMovingAverageService, SpeedMovingAverageService>();
        builder.Services.AddSingleton<FileTransferService>();
        builder.Services.AddSingleton<SessionsService>();
        builder.Services.AddSingleton<ChannelsService>();
        builder.Services.AddSingleton<TorrentService>();
        builder.Services.Configure<RTSharp.Daemon.Services.Prometheus.PrometheusOptions>(builder.Configuration.GetSection("Prometheus"));
        builder.Services.AddSingleton<RTSharp.Daemon.Services.Prometheus.PrometheusMetricsService>();
        builder.Services.AddHostedService(services => services.GetRequiredService<RTSharp.Daemon.Services.Prometheus.PrometheusMetricsService>());

        foreach (var key in builder.Configuration.GetSection("DataProviders:rtorrent").GetChildren().Select(x => x.Key)) {
            builder.Services.Configure<RTSharp.Daemon.Services.rtorrent.ConfigModel>(key, builder.Configuration.GetSection("DataProviders:rtorrent:" + key));
    
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.TorrentPoll.TorrentPolling>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.SCGICommunication>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.Grpc>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.TorrentOpService>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.SettingsService>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.rtorrent.RtorrentMonitor>(key);
            builder.Services.AddHostedService(services => services.GetRequiredKeyedService<RTSharp.Daemon.Services.rtorrent.RtorrentMonitor>(key));
            builder.Services.AddHostedService(services => services.GetRequiredKeyedService<RTSharp.Daemon.Services.rtorrent.TorrentPoll.TorrentPolling>(key));
    
            Console.WriteLine($"Registered data provider '{key}'");
    
            builder.Services.AddKeyedSingleton(key, (services, key) => new RTSharp.Daemon.GRPCServices.DataProvider.RegisteredDataProvider(services, RTSharp.Daemon.GRPCServices.DataProvider.DataProviderType.rtorrent, (string)key));
        }

        foreach (var key in builder.Configuration.GetSection("DataProviders:qbittorrent").GetChildren().Select(x => x.Key)) {
            builder.Services.Configure<RTSharp.Daemon.Services.qbittorrent.ConfigModel>(key, builder.Configuration.GetSection("DataProviders:qbittorrent:" + key));

            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.qbittorrent.Grpc>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.qbittorrent.SettingsGrpc>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.qbittorrent.QbitClient>(key);

            Console.WriteLine($"Registered data provider '{key}'");

            builder.Services.AddKeyedSingleton(key, (services, key) => new RTSharp.Daemon.GRPCServices.DataProvider.RegisteredDataProvider(services, RTSharp.Daemon.GRPCServices.DataProvider.DataProviderType.qbittorrent, (string)key));
        }

        foreach (var key in builder.Configuration.GetSection("DataProviders:transmission").GetChildren().Select(x => x.Key)) {
            builder.Services.Configure<RTSharp.Daemon.Services.transmission.ConfigModel>(key, builder.Configuration.GetSection("DataProviders:transmission:" + key));

            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.transmission.Grpc>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.transmission.SettingsGrpc>(key);
            builder.Services.AddKeyedSingleton<RTSharp.Daemon.Services.transmission.TransmissionClient>(key);

            Console.WriteLine($"Registered data provider '{key}'");

            builder.Services.AddKeyedSingleton(key, (services, key) => new RTSharp.Daemon.GRPCServices.DataProvider.RegisteredDataProvider(services, RTSharp.Daemon.GRPCServices.DataProvider.DataProviderType.transmission, (string)key));
        }

        builder.Services.AddSingleton<RTSharp.Daemon.GRPCServices.DataProvider.RegisteredDataProviders>();

        var setgid = builder.Configuration.GetSection("setgid").Get<string?>();

        if (setgid != null) {
            if (Mono.Unix.Native.Syscall.setgid(UInt32.Parse(setgid)) != 0) {
                throw new Exception($"failed to setgid {UInt32.Parse(setgid)}. {Mono.Unix.Native.Stdlib.GetLastError()}");
            } else {
                Console.WriteLine("setgid: " + UInt32.Parse(setgid));
            }
        }

        var setuid = builder.Configuration.GetSection("setuid").Get<string?>();

        if (setuid != null) {
            if (Mono.Unix.Native.Syscall.setuid(UInt32.Parse(setuid)) != 0) {
                throw new Exception($"failed to setuid {UInt32.Parse(setuid)}. {Mono.Unix.Native.Stdlib.GetLastError()}");
            } else {
                Console.WriteLine("setuid: " + UInt32.Parse(setuid));
            }
        }

        string[] allowedClients;

        void loadConfig()
        {
            allowedClients = builder.Configuration.GetSection("AllowedClients").Get<string[]>() ?? [];

            var certs = builder.Configuration.GetCertificates();
            ServerCertificate = new ReloadableServerCertificate(certs);
            ServerCertificate.Reload();
        }

        loadConfig();

        builder.WebHost.ConfigureKestrel(kestrelServerOptions => {
            foreach (var address in builder.Configuration.GetListenAddresses()) {
                kestrelServerOptions.Listen(IPEndPoint.Parse(address), cfg => {
                    cfg.UseHttps(opts => {
                        opts.ServerCertificateSelector = (ctx, sni) => ServerCertificate.Current;
                        opts.OnAuthenticate = (ctx, innerOpts) => {
                            var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<KestrelServer>>();
                            innerOpts.ClientCertificateRequired = true;
                            innerOpts.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                                var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<KestrelServer>>();

                                if (certificate == null) {
                                    logger.LogWarning($"Client {ctx.RemoteEndPoint!.ToString()} disallowed (no certificate)");
                                    return false;
                                }

                                var clientThumbprint = certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

                                if (allowedClients?.Any() != true && false /* disable, allowing client depends on one button press which is not great */) {
                                    Console.WriteLine();
                                    Console.WriteLine("You have no allowed clients set up, but a client is attempting to connect to");
                                    Console.WriteLine("the server.");
                                    Console.WriteLine();
                                    Console.WriteLine("Client thumbprint: ");
                                    Console.WriteLine(clientThumbprint);
                                    Console.WriteLine();
                                    Console.Write("Allow client? [Y/N]: ");
                                    var key = Console.ReadKey();
                                    if (key.KeyChar != 'Y' && key.KeyChar != 'y')
                                        return false;

                                    return true;
                                }

                                if (allowedClients != null && allowedClients.Any(x => x.Equals(clientThumbprint, StringComparison.OrdinalIgnoreCase))) {
                                    logger.LogInformation($"Client {ctx.RemoteEndPoint!.ToString()} (thumbprint {clientThumbprint}) connected");
                                    return true;
                                }
                        
                                logger.LogWarning($"Client {ctx.RemoteEndPoint!.ToString()} (thumbprint {clientThumbprint}) disallowed");
                                return false;
                            };
                        };
                        opts.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate;
                    });
                });
            }
        });

        var app = builder.Build();

        PosixSignalRegistration.Create(PosixSignal.SIGHUP, context => {
            context.Cancel = true;
            loadConfig();

            Console.WriteLine("Server configuration reloaded");
        });

        app.MapGrpcService<RTSharp.Daemon.GRPCServices.FilesService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.ServerService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.TorrentsService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.TorrentService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.StatsService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.RtorrentSettingsService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.QBittorrentSettingsService>();
        app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.TransmissionSettingsService>();

        var prometheusOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<RTSharp.Daemon.Services.Prometheus.PrometheusOptions>>().Value;
        if (prometheusOptions.Enabled) {
            app.MapGet(prometheusOptions.Path, (RTSharp.Daemon.Services.Prometheus.PrometheusMetricsService metrics) => Results.Text(metrics.Render(), "text/plain; charset=utf-8"));
        }

        app.Run();
    }

}
