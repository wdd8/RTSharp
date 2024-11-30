using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.ClientFactory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Plugin;
using RTSharp.Shared.Abstractions;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using RTSharp.Daemon;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Utils;

namespace RTSharp.Core.Services.Daemon
{
    public class DaemonTorrentsService : IDaemonTorrentsService
    {
        GRPCTorrentService.GRPCTorrentServiceClient TorrentClient;
        GRPCTorrentsService.GRPCTorrentsServiceClient TorrentsClient;
        ILogger<DaemonService> Logger;

        private readonly string ServerId;
        private readonly DataProvider DataProvider;

        public DaemonTorrentsService(DataProvider DataProvider)
        {
            this.ServerId = DataProvider.DataProviderInstanceConfig.ServerId;
            this.DataProvider = DataProvider;

            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();

            TorrentClient = clientFactory.CreateClient<GRPCTorrentService.GRPCTorrentServiceClient>(nameof(GRPCTorrentService.GRPCTorrentServiceClient) + "_" + ServerId);
            TorrentsClient = clientFactory.CreateClient<GRPCTorrentsService.GRPCTorrentsServiceClient>(nameof(GRPCTorrentsService.GRPCTorrentsServiceClient) + "_" + ServerId);

            Logger = scope.ServiceProvider.GetRequiredService<ILogger<DaemonService>>();
        }

        public Channel<ListingChanges<Shared.Abstractions.Torrent, byte[]>> GetTorrentChanges(CancellationToken CancellationToken)
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var config = scope.ServiceProvider.GetRequiredService<Core.Config>();

            var updates = TorrentsClient.GetTorrentListUpdates(new GetTorrentListUpdatesRequest {
                Interval = Duration.FromTimeSpan(DataProvider.DataProviderInstanceConfig.ListUpdateInterval)
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());

            var channel = System.Threading.Channels.Channel.CreateUnbounded<ListingChanges<Shared.Abstractions.Torrent, byte[]>>(new System.Threading.Channels.UnboundedChannelOptions() {
                SingleReader = true,
                SingleWriter = true
            });

            _ = Task.Run(async () => {
                try {
                    DataProvider.State.Change(DataProviderState.ACTIVE);

                    await foreach (var update in updates.ResponseStream.ReadAllAsync(CancellationToken)) {
                        var changes = new List<Shared.Abstractions.Torrent>();

                        var incomplete = update.Incomplete.Select(x => {
                            var hash = x.Hash.ToByteArray();
                            var cachedTorrent = Core.TorrentPolling.TorrentPolling.Torrents.FirstOrDefault(i => i.Hash.SequenceEqual(hash) && i.Owner == DataProvider);

                            if (cachedTorrent == null) {
                                return null; // TODO: what to do?
                            }

                            return Mapper.MapFromProto(x, cachedTorrent);
                        });
                        changes.AddRange(incomplete);

                        var complete = update.Complete.Select(x => {
                            var hash = x.Hash.ToByteArray();
                            var cachedTorrent = Core.TorrentPolling.TorrentPolling.Torrents.FirstOrDefault(i => i.Hash.SequenceEqual(hash) && i.Owner == DataProvider);

                            if (cachedTorrent == null) {
                                return null; // TODO: what to do?
                            }

                            return Mapper.MapFromProto(x, cachedTorrent);
                        });
                        changes.AddRange(complete);

                        var fullUpdate = update.FullUpdate.Select(Mapper.MapFromProto);
                        changes.AddRange(fullUpdate);

                        await channel.Writer.WriteAsync(new ListingChanges<Shared.Abstractions.Torrent, byte[]>(changes, update.Removed.Select(x => x.ToByteArray())), CancellationToken);
                    }

                    channel.Writer.Complete();
                } catch (Exception ex) {
                    // TODO: log or additional handling
                    channel.Writer.Complete(ex);
                } finally {
                    DataProvider.State.Change(DataProviderState.INACTIVE);
                }
            });

            return channel;
        }

        private static TorrentStatuses FromReply(TorrentsReply In)
        {
            var ret = new TorrentStatuses();
            foreach (var torrent in In.Torrents)
            {
                ret.Add((torrent.InfoHash.ToByteArray(), torrent.Status.Select(x => x.FaultCode is null or "0" ? null : new Exception(x.Command + ": Code " + x.FaultCode + " - " + x.FaultString)).ToList()));
            }
            
            return ret;
        }

        public async Task<TorrentStatuses> RemoveTorrentsAndData(IList<byte[]> Hashes)
        {
            var result = await TorrentClient.RemoveTorrentsAndDataAsync(new Torrents {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return FromReply(result);
        }
        
        public async Task<InfoHashDictionary<IList<Tracker>>> GetTorrentsTrackers(IList<byte[]> Hashes)
        {
            var result = await TorrentClient.GetTorrentsTrackersAsync(new Torrents
            {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            return result.Reply.ToInfoHashDictionary(x => x.InfoHash.ToByteArray(), IList<Tracker> (x) => x.Trackers.Select(i => new Tracker
            {
                ID = i.ID,
                Uri = new Uri(i.Uri),
                Status = FlagsMapper.Map(i.Status, a => a switch {
                    TorrentTrackerStatus.Active => Tracker.TRACKER_STATUS.ACTIVE,
                    TorrentTrackerStatus.NotActive => Tracker.TRACKER_STATUS.NOT_ACTIVE,
                    TorrentTrackerStatus.NotContactedYet => Tracker.TRACKER_STATUS.NOT_CONTACTED_YET,
                    TorrentTrackerStatus.Disabled => Tracker.TRACKER_STATUS.DISABLED,
                    TorrentTrackerStatus.Enabled => Tracker.TRACKER_STATUS.ENABLED,
                    _ => throw new ArgumentOutOfRangeException()
                }),
                Seeders = i.Seeders,
                Peers = i.Peers,
                Downloaded = i.Downloaded,
                LastUpdated = i.LastUpdated.ToDateTime(),
                Interval = i.ScrapeInterval.ToTimeSpan(),
                StatusMsg = i.StatusMessage
            }).ToList());
        }
        
        public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> Hashes)
        {
            var res = TorrentClient.GetDotTorrents(new Torrents
            {
                Hashes = { Hashes.Select(x => x.ToByteString()) }
            }, headers: DataProvider.GetBuiltInDataProviderGrpcHeaders());
            
            var meta = new Dictionary<string, byte[]>();
            var dotTorrents = new Dictionary<string, MemoryStream>();

            await foreach (var data in res.ResponseStream.ReadAllAsync()) {
                switch (data.DataCase) {
                    case DotTorrentsData.DataOneofCase.TMetadata:
                        if (meta.ContainsKey(data.TMetadata.Id)) {
                            throw new InvalidOperationException("Received duplicate metadata on hash");
                        }

                        dotTorrents[data.TMetadata.Id] = new MemoryStream();
                        meta[data.TMetadata.Id] = data.TMetadata.Hash.ToByteArray();
                        break;
                    case DotTorrentsData.DataOneofCase.TData:
                        dotTorrents[data.TData.Id].Write(data.TData.Chunk.Span);
                        break;
                }
            }

            var ret = new InfoHashDictionary<byte[]>();
            foreach (var (k, v) in dotTorrents) {
                ret[meta[k]] = v.ToArray();
                v.Dispose();
            }
            
            return ret;
        }
    }
}
