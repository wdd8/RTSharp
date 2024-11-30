using Grpc.Core;
using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.GRPCServices.DataProvider
{
    public partial class TorrentService(ILogger<TorrentService> Logger, Services.rtorrent.Grpc RtorrentGrpc, IServiceScopeFactory ScopeFactory) : GRPCTorrentService.GRPCTorrentServiceBase
    {
        public override async Task<TorrentsReply> StartTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.StartTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> PauseTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.PauseTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> StopTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.StopTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> ForceRecheckTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.ForceRecheckTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> ReannounceToAllTrackers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.ReannounceToAllTrackers(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        record Torrent(string Path, string Filename, MemoryStream Data);

        public override async Task<TorrentsReply> AddTorrents(IAsyncStreamReader<NewTorrentsData> Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.AddTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsFilesReply> GetTorrentsFiles(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.GetTorrentsFiles(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> RemoveTorrents(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.RemoveTorrents(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsReply> RemoveTorrentsAndData(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.RemoveTorrentsAndData(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task GetDotTorrents(Torrents Req, IServerStreamWriter<DotTorrentsData> Res, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            switch (dp) {
                case DataProviderName.rtorrent:
                    await RtorrentGrpc.GetDotTorrents(Req, Res, Ctx.CancellationToken);
                    break;
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            };
        }

        public override async Task<TorrentsPeersReply> GetTorrentsPeers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.GetTorrentsPeers(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task MoveDownloadDirectory(MoveDownloadDirectoryArgs Req, IServerStreamWriter<MoveDownloadDirectoryProgress> Res, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            switch (dp) {
                case
                DataProviderName.rtorrent:
                    await RtorrentGrpc.MoveDownloadDirectory(Req, Res, Ctx.CancellationToken);
                    break;
                default:
                    throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"));
            };
        }

        public override async Task<TorrentsReply> SetLabels(SetLabelsArgs Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.SetLabels(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }

        public override async Task<TorrentsPiecesReply> GetTorrentsPieces(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.GetTorrentsPieces(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
        
        public override async Task<TorrentsTrackersReply> GetTorrentsTrackers(Torrents Req, ServerCallContext Ctx)
        {
            var dp = Utils.GetDataProviderName(Ctx);
            return dp switch {
                DataProviderName.rtorrent => await RtorrentGrpc.GetTorrentsTrackers(Req),
                _ => throw new RpcException(new Grpc.Core.Status(StatusCode.InvalidArgument, "Unknown data provider"))
            };
        }
    }
}
