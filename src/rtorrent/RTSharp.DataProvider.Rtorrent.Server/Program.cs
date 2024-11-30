using System;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RTSharp.DataProvider.Rtorrent.Server;

class Program
{
    public static string InstanceName;

    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();

        builder.Services.AddAuthentication(CertificateAuthenticationDefaults.AuthenticationScheme)
            .AddCertificate(options => {
                options.AllowedCertificateTypes = CertificateTypes.SelfSigned;
                options.RevocationMode = X509RevocationMode.Offline;

                /*options.Events = new CertificateAuthenticationEvents {
                    OnCertificateValidated = DevelopmentModeCertificateHelper.Validate
                };*/
            });
        builder.Services.AddAuthorization();
        builder.Services.AddGrpc();
        builder.Services.AddScoped<SCGICommunication>();
        builder.Services.AddScoped<Services.TorrentService>();

        InstanceName = builder.Configuration.GetSection("InstanceName").Get<string>();
        var listenAddresses = builder.Configuration.GetSection("ListenAddress").Get<string[]>();
        var allowedClients = builder.Configuration.GetSection("AllowedClients").Get<string[]>();

        var publicPem = await System.IO.File.ReadAllTextAsync(builder.Configuration.GetSection("Certificate").GetValue<string>("PublicPem"));
        var privatePem = await System.IO.File.ReadAllTextAsync(builder.Configuration.GetSection("Certificate").GetValue<string>("PrivatePem"));
        var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);
        var cert = new X509Certificate2(x509.Export(X509ContentType.Pkcs12));

        builder.WebHost.ConfigureKestrel(kestrelServerOptions => {
            foreach (var address in listenAddresses) {
                kestrelServerOptions.Listen(IPEndPoint.Parse(address), cfg => {
                    cfg.UseHttps(async (SslStream stream, SslClientHelloInfo clientHelloInfo, object state, CancellationToken cancellationToken) =>
                    {
                        var ops = new SslServerAuthenticationOptions {
                            ClientCertificateRequired = true,
                            CertificateChainPolicy = null,
                            EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                            ServerCertificate = cert,
                            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                                if (certificate == null)
                                    return true;

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

                                if (allowedClients?.Contains(clientThumbprint) != true)
                                    return false;

                                return true;
                            }
                        };

                        return ops;
                    }, null);
                });
            }
        });

        var app = builder.Build();

        if (app.Environment.IsDevelopment()) {
            app.UseDeveloperExceptionPage();
        }

        app.UseRouting();

        app.MapGrpcService<GRPCServices.TorrentService>();
        app.MapGet("/", async context =>
        {
            await context.Response.WriteAsync("Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");
        });

        await app.RunAsync();
    }
}