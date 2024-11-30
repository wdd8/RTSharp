using Microsoft.Extensions.DependencyInjection;

using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Services;
using RTSharp.Daemon.Utils;

using System.Net;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

Utils.RtorrentEnabled = builder.Configuration.GetSection("DataProviders:rtorrent").Exists();

builder.Services.AddGrpc();
builder.Services.AddSingleton<FileTransferService>();
builder.Services.AddSingleton<SessionsService>();
builder.Services.AddSingleton<ChannelsService>();

builder.Services.AddSingleton<RTSharp.Daemon.Services.qbittorrent.QbitClient>();

if (Utils.RtorrentEnabled) {
    builder.Services.AddSingleton<RTSharp.Daemon.Services.rtorrent.TorrentPoll.TorrentPolling>();
    builder.Services.AddSingleton<RTSharp.Daemon.Services.rtorrent.SCGICommunication>();
    builder.Services.AddTransient<RTSharp.Daemon.Services.rtorrent.Grpc>();
    builder.Services.AddSingleton<RTSharp.Daemon.Services.rtorrent.TorrentOpService>();
    builder.Services.AddSingleton<RTSharp.Daemon.Services.rtorrent.SettingsService>();
    builder.Services.AddHostedService<RTSharp.Daemon.Services.rtorrent.RtorrentMonitor>();
    builder.Services.AddHostedService(services => services.GetRequiredService<RTSharp.Daemon.Services.rtorrent.TorrentPoll.TorrentPolling>());
}

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

app.MapGrpcService<FilesService>();
app.MapGrpcService<ServerService>();
app.MapGrpcService<TorrentsService>();
app.MapGrpcService<TorrentService>();
if (Utils.RtorrentEnabled) {
    app.MapGrpcService<RtorrentSettingsService>();
}

app.Run();
