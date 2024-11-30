using Grpc.Net.Client;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using RTSharp.Daemon.Protocols;

namespace RTSharp.Daemon.Services;

public class ChannelsService(IConfiguration Config, ILogger<ChannelsService> Logger)
{
    private readonly Dictionary<string, GrpcChannel> ChannelsCache = new();

    public async ValueTask<GrpcChannel> GetChannel(string Url)
    {
        if (ChannelsCache.TryGetValue(Url, out var ret))
            return ret;

        var publicPem = await System.IO.File.ReadAllTextAsync(Config.GetSection("Certificate").GetValue<string>("PublicPem"));
        var privatePem = await System.IO.File.ReadAllTextAsync(Config.GetSection("Certificate").GetValue<string>("PrivatePem"));
        var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);
        var cert = new X509Certificate2(x509.Export(X509ContentType.Pkcs12));
        var allowedClients = Config.GetSection("AllowedClients").Get<string[]>();

        Logger.LogInformation($"Creating channel with certificate {cert.GetCertHashString(HashAlgorithmName.SHA256)}");

        return ChannelsCache[Url] = GrpcChannel.ForAddress(Url, new GrpcChannelOptions() {
            HttpHandler = new SocketsHttpHandler() {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                    ClientCertificates = [cert],
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                        if (certificate == null)
                            return false;

                        var clientThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);

                        if (allowedClients != null && !allowedClients.Any(x => x.Equals(clientThumbprint, StringComparison.OrdinalIgnoreCase))) {
                            Logger.LogWarning($"Tried to connect to remote server, but server thumbprint {clientThumbprint} is not allowed");
                            return false;
                        }

                        return true;
                    }
                }
            }
        });
    }

    public async ValueTask<GRPCFilesService.GRPCFilesServiceClient> GetFilesClient(string Url)
    {
        var channel = await GetChannel(Url);
        return new GRPCFilesService.GRPCFilesServiceClient(channel);
    }

    public async ValueTask<GRPCServerService.GRPCServerServiceClient> GetServerClient(string Url)
    {
        var channel = await GetChannel(Url);
        return new GRPCServerService.GRPCServerServiceClient(channel);
    }
}
