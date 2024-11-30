using Grpc.Core;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public enum DataProviderName
    {
        rtorrent,
        qbittorrent,
        transmission
    }

    public static class Utils
    {
        public static bool RtorrentEnabled;

        public static DataProviderName GetDataProviderName(ServerCallContext Ctx)
        {
            var dpNameRaw = Ctx.RequestHeaders.FirstOrDefault(x => x.Key == "data-provider") ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "data-provider header missing"));

            var ret = dpNameRaw.Value switch {
                "rtorrent" => DataProviderName.rtorrent,
                "qbittorrent" => DataProviderName.qbittorrent,
                "transmission" => DataProviderName.transmission,
                _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "data-provider header unknown"))
            };

            switch (ret) {
                case DataProviderName.rtorrent:
                    if (!RtorrentEnabled)
                        throw new RpcException(new Status(StatusCode.FailedPrecondition, "rtorrent config missing"));
                    break;
                default:
                    throw new RpcException(new Status(StatusCode.FailedPrecondition, "config missing"));
            }

            return ret;
        }
    }
}
