using Grpc.Net.Client;
using System.Security.Cryptography;
using RTSharp.Daemon.Protocols;
using System.Collections.Concurrent;

namespace RTSharp.Daemon.Services;

public class ChannelsService(IConfiguration Config, ILogger<ChannelsService> Logger)
{
    private readonly ConcurrentDictionary<string, GrpcChannel> ChannelsCache = new();

    public GrpcChannel GetChannel(string Url)
    {
        return ChannelsCache.GetOrAdd(Url, Url => {
            var allowedClients = Config.GetSection("AllowedClients").Get<string[]>() ?? [];

            Logger.LogDebug($"Creating channel with certificate {Program.ServerCertificate.Current!.GetCertHashString(HashAlgorithmName.SHA256)}");

            return ChannelsCache[Url] = GrpcChannel.ForAddress(Url, new GrpcChannelOptions() {
                HttpHandler = new SocketsHttpHandler() {
                    InitialHttp2StreamWindowSize = 16 * 1024 * 1024,
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                        ClientCertificates = [Program.ServerCertificate.Current!],
                        RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => {
                            if (certificate == null)
                                return false;

                            var clientThumbprint = certificate.GetCertHashString(HashAlgorithmName.SHA256);

                            if (!allowedClients.Any(x => x.Equals(clientThumbprint, StringComparison.OrdinalIgnoreCase))) {
                                Logger.LogWarning($"Tried to connect to remote server, but server thumbprint {clientThumbprint} is not allowed");
                                return false;
                            }

                            return true;
                        }
                    }
                }
            });
        });
    }

    public GRPCFilesService.GRPCFilesServiceClient GetFilesClient(string Url)
    {
        var channel = GetChannel(Url);
        return new GRPCFilesService.GRPCFilesServiceClient(channel);
    }

    public GRPCServerService.GRPCServerServiceClient GetServerClient(string Url)
    {
        var channel = GetChannel(Url);
        return new GRPCServerService.GRPCServerServiceClient(channel);
    }

    public RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient GetTorrentServiceClient(string Url)
    {
        var channel = GetChannel(Url);
        return new RTSharp.Daemon.Protocols.DataProvider.GRPCTorrentService.GRPCTorrentServiceClient(channel);
    }
}
