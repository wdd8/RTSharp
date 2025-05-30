using Grpc.Core;
using RTSharp.Daemon.GRPCServices.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.Services;

public class TorrentService
{
    public async Task<TorrentsFilesReply> GetTorrentsFiles(RegisteredDataProvider RegisteredDataProvider, Torrents Req)
    {
        return RegisteredDataProvider.Type switch {
            DataProviderType.rtorrent => await RegisteredDataProvider.Resolve<Services.rtorrent.Grpc>().GetTorrentsFiles(Req),
            _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
        };
    }
}