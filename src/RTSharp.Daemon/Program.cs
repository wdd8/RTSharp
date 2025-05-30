using RTSharp.Daemon.Services;
using RTSharp.Daemon.Utils;

using System.Net;
using System.Security.Cryptography.X509Certificates;

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


var allowedClients = builder.Configuration.GetSection("AllowedClients").Get<string[]>();

var certs = builder.Configuration.GetCertificates();

var publicPem = await System.IO.File.ReadAllTextAsync(certs.Public);
var privatePem = await System.IO.File.ReadAllTextAsync(certs.Private);
var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);

builder.WebHost.ConfigureKestrel(kestrelServerOptions => {
    foreach (var address in builder.Configuration.GetListenAddresses()) {
        kestrelServerOptions.Listen(IPEndPoint.Parse(address), cfg => {
            cfg.UseHttps(x509, opts => {
                opts.OnAuthenticate = (ctx, innerOpts) => {
                    var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<Program>>();
                    innerOpts.ClientCertificateRequired = true;
                    innerOpts.RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                        if (certificate == null) {
                            var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<Program>>();
                            logger.LogWarning($"Client {ctx.RemoteEndPoint!.ToString()} disallowed (no certificate)");
                            return false;
                        }

                        var clientThumbprint = certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

                        if (allowedClients?.Any() != true) {
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

                        if (allowedClients != null && !allowedClients.Any(x => x.Equals(clientThumbprint, StringComparison.OrdinalIgnoreCase))) {
                            var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<Program>>();
                            logger.LogWarning($"Client {ctx.RemoteEndPoint!.ToString()} (thumbprint {clientThumbprint}) disallowed");
                            return false;
                        }

                        logger.LogInformation($"Client {ctx.RemoteEndPoint!.ToString()} (thumbprint {clientThumbprint}) connected");
                        return true;
                    };
                };
                opts.ClientCertificateMode = Microsoft.AspNetCore.Server.Kestrel.Https.ClientCertificateMode.NoCertificate;
            });
        });
    }
});

var app = builder.Build();

app.MapGrpcService<RTSharp.Daemon.GRPCServices.FilesService>();
app.MapGrpcService<RTSharp.Daemon.GRPCServices.ServerService>();
app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.TorrentsService>();
app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.TorrentService>();
app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.RtorrentSettingsService>();
app.MapGrpcService<RTSharp.Daemon.GRPCServices.DataProvider.QBittorrentSettingsService>();

app.Run();
