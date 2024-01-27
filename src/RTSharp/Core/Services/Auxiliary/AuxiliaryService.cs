using Avalonia;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.ClientFactory;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using RTSharp.Auxiliary.Protocols;
using RTSharp.Shared.Abstractions;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace RTSharp.Core.Services.Auxiliary
{
    public class AuxiliaryService : IAuxiliaryService
    {
        GRPCServerService.GRPCServerServiceClient ServerClient;
        GRPCFilesService.GRPCFilesServiceClient FilesClient;
        ILogger<AuxiliaryService> Logger;

        private Dictionary<string, Server> Servers;

        public AuxiliaryService(string ServerId)
        {
            using var scope = Core.ServiceProvider.CreateScope();
            var clientFactory = scope.ServiceProvider.GetRequiredService<GrpcClientFactory>();
            var config = scope.ServiceProvider.GetRequiredService<Config>();

            ServerClient = clientFactory.CreateClient<GRPCServerService.GRPCServerServiceClient>(nameof(GRPCServerService.GRPCServerServiceClient) + "_" + ServerId);
            FilesClient = clientFactory.CreateClient<GRPCFilesService.GRPCFilesServiceClient>(nameof(GRPCFilesService.GRPCFilesServiceClient) + "_" + ServerId);
            Servers = config.Servers.Value;
            Logger = scope.ServiceProvider.GetRequiredService<ILogger<AuxiliaryService>>();
        }

        public async Task Ping()
        {
            await ServerClient.TestAsync(new Empty());
        }

        public async Task RequestRecieveFiles(IEnumerable<(string RemoteSource, string StoreTo)> Paths, string SenderServerId, IProgress<(string File, ulong BytesTransferred)> Progress)
        {
            var session = await FilesClient.ReceiveFilesFromRemoteAsync(new ReceiveFilesFromRemoteInput {
                RemoteSourcePaths = { Paths.Select(x => x.RemoteSource) },
                StorePaths = { Paths.Select(x => x.StoreTo) },
                TargetUrl = Servers[SenderServerId].GetUri().OriginalString
            });

            var progress = FilesClient.FileTransferSessionsProgress(new Empty());

            await foreach (var info in progress.ResponseStream.ReadAllAsync()) {
                if (info.SessionId != session.SessionId)
                    continue;

                if (info.File == "")
                    return;

                Progress.Report((info.File, info.BytesReceived));
            }
        }

        public async Task<Shared.Abstractions.FileSystemItem> GetDirectoryInfo(string Path)
        {
            RTSharp.Auxiliary.Protocols.FileSystemItem dir;
            try {
                dir = await FilesClient.GetDirectoryInfoAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to GetDirectoryInfo");
                throw;
            }

            static Shared.Abstractions.FileSystemItem map(RTSharp.Auxiliary.Protocols.FileSystemItem In)
            {
                return new Shared.Abstractions.FileSystemItem(
                    Path: In.Path,
                    Directory: In.Directory,
                    LastModified: In.LastModified.ToDateTime(),
                    Permissions: In.Permissions,
                    Size: In.Size,
                    Children: In.Children.Select(map).ToList()
                );
            }

            return map(dir);
        }

        public async Task CreateDirectory(string Path)
        {
            try {
                await FilesClient.CreateDirectoryAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to CreateDirectory");
                throw;
            }
        }

        public async Task<Dictionary<string, bool>> CheckExists(IList<string> Files)
        {
            try {
                return (await FilesClient.CheckExistsAsync(new CheckExistsInput {
                    Paths = { Files }
                })).Existence.ToDictionary(x => x.Key, x => x.Value);
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to CheckExists");
                throw;
            }
        }

        public async Task RemoveEmptyDirectory(string Path)
        {
            try {
                await FilesClient.RemoveEmptyDirectoryAsync(new StringValue { Value = Path });
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to RemoveEmptyDirectory");
                throw;
            }
        }

        public async Task<IList<string>> Mediainfo(IList<string> Files)
        {
            MediaInfoReply reply;
            try {
                reply = await FilesClient.MediaInfoAsync(new MediaInfoInput() {
                    Paths = { Files }
                });
            } catch (Exception ex) {
                Logger.LogError(ex, "Failed to Mediainfo");
                throw;
            }

            return reply.Output;
        }
    }
}
