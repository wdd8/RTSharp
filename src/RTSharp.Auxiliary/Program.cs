using Microsoft.Extensions.Logging;

using RTSharp.Auxiliary.Services;

using System.Net;
using System.Net.Security;
using System.Runtime.ConstrainedExecution;
using System.Security.Cryptography.X509Certificates;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
builder.Services.AddSingleton<FileTransferService>();

var listenAddresses = builder.Configuration.GetSection("ListenAddress").Get<string[]>()!;

var allowedClients = builder.Configuration.GetSection("AllowedClients").Get<string[]>();

var publicPem = await System.IO.File.ReadAllTextAsync(builder.Configuration.GetSection("Certificate").GetValue<string>("PublicPem"));
var privatePem = await System.IO.File.ReadAllTextAsync(builder.Configuration.GetSection("Certificate").GetValue<string>("PrivatePem"));
var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);
var cert = new X509Certificate2(x509.Export(X509ContentType.Pkcs12));

builder.WebHost.ConfigureKestrel(kestrelServerOptions => {
    foreach (var address in listenAddresses) {
        kestrelServerOptions.Listen(IPEndPoint.Parse(address), cfg => {
            cfg.UseHttps(async (SslStream stream, SslClientHelloInfo clientHelloInfo, object? state, CancellationToken cancellationToken) =>
            {
                var ops = new SslServerAuthenticationOptions {
                    ClientCertificateRequired = true,
                    ServerCertificate = cert,
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                        if (certificate == null) {
                            var logger = kestrelServerOptions.ApplicationServices.GetRequiredService<ILogger<Program>>();
                            logger.LogWarning($"Client disallowed (no certificate)");
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
                            logger.LogWarning($"Client (thumbprint {clientThumbprint}) disallowed");
                            return false;
                        }

                        return true;
                    }
                };

                return ops;
            }, null);
        });
    }
});

var app = builder.Build();

app.MapGrpcService<FilesService>();
app.MapGrpcService<ServerService>();

app.Run();
