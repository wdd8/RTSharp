using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using RTSharp.Daemon.Protocols;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Plugin;
using RTSharp.Scripts;
using RTSharp.Scripts.RequestReceiveFiles;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using Serilog;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using ScriptMessage = RTSharp.Shared.Utils.ScriptMessage;
using ScriptProgressState = RTSharp.Shared.Abstractions.ScriptProgressState;

namespace RTSharp.Core.Services.Daemon;

[Singleton]
public class DaemonService : IDaemonService
{
    /// See <see cref="ConfigureServices.CreateChannel(string, Config.Models.Server, Func{System.Security.Cryptography.X509Certificates.X509Certificate2, bool}?)" />
    /// 
    /// Populated from the TLS validation callback in ConfigureServices.CreateChannel. A
    /// cleaner solution would be to establish a connection, then extract the peer
    /// certificate from that channel — but gRPC doesn't expose any accessors for that
    public static ConcurrentDictionary<string, System.Security.Cryptography.X509Certificates.X509Certificate2> RemoteCerts { get; } = new();

    public static ConcurrentDictionary<string, string> RemoteCipherSuites { get; } = new();

    readonly GRPCServerService.GRPCServerServiceClient ServerClient;
    readonly GRPCFilesService.GRPCFilesServiceClient FilesClient;
    readonly GRPCLogsService.GRPCLogsServiceClient LogsClient;
    readonly IHostApplicationLifetime Lifetime;
    readonly GrpcChannel Channel;
    readonly CancellationTokenSource Cts;

    public string Id { get; }

    public string Host { get; }

    public ushort Port { get; }

    public Uri Uri { get; }

    public CancellationToken CancellationToken => Cts.Token;

    public Notifyable<TimeSpan> Latency { get; private set; } = new();

    public DaemonService(string ServerId, Config.Models.Server Server, GrpcChannel Channel, IHostApplicationLifetime Lifetime)
    {
        this.Id = ServerId;
        this.Lifetime = Lifetime;
        this.Channel = Channel;
        this.Cts = new CancellationTokenSource();

        ServerClient = new GRPCServerService.GRPCServerServiceClient(Channel);
        FilesClient = new GRPCFilesService.GRPCFilesServiceClient(Channel);
        LogsClient = new GRPCLogsService.GRPCLogsServiceClient(Channel);

        Host = Server.Host;
        Port = Server.DaemonPort;

        var builder = new UriBuilder {
            Host = Host,
            Port = Port,
            Scheme = "https"
        };
        Uri = builder.Uri;

        _ = Task.Run(PingLoop);
    }

    public void Shutdown()
    {
        Cts.Cancel();
        Channel.Dispose();
    }

    public async Task PingLoop()
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(Cts.Token, Lifetime.ApplicationStopping);
        while (!linked.Token.IsCancellationRequested) {
            var sw = Stopwatch.StartNew();
            try {
                await Ping(linked.Token);
                sw.Stop();
                Latency.Change(sw.Elapsed);
            } catch (OperationCanceledException) when (linked.Token.IsCancellationRequested) {
                break;
            } catch {
                sw.Stop();
                Log.Logger.Error($"Ping failed for {Host}:{Port}");
            }

            var remaining = TimeSpan.FromSeconds(1) - sw.Elapsed;
            if (remaining > TimeSpan.Zero) {
                try {
                    await Task.Delay(remaining, linked.Token);
                } catch (OperationCanceledException) when (linked.Token.IsCancellationRequested) {
                    break;
                }
            }
        }
    }

    public async Task Ping(CancellationToken cancellationToken)
    {
        await ServerClient.TestAsync(new Empty(), cancellationToken: cancellationToken);
    }

    public async Task RequestReceiveFiles(IEnumerable<(string RemoteSource, string StoreTo, ulong TotalSize)> Paths, string SenderServerId, Action<(string File, float Progress)> Progress)
    {
        using var scope = ServiceProvider.CreateScope();
        var config = scope.ServiceProvider.GetRequiredService<Config>();
        var senderServer = config.Servers.Value[SenderServerId].GetUri();

        var script = await ServerClient.StartScriptAsync(new StartScriptInput {
            Script = DaemonScripts.RequestReceiveFiles,
            Name = "RequestReceiveFiles",
            Input = {
                ScriptMessage.Of(new RequestReceiveFilesInput(
                    senderServer.ToString(),
                    [.. Paths.Select(x => new RequestReceiveFilePath(x.StoreTo, x.RemoteSource, x.TotalSize))]
                )).ToProtocol()
            }
        });

        var id = script.Id;

        var progress = ServerClient.ScriptStatus(new BytesValue() { Value = id });

        await foreach (var info in progress.ResponseStream.ReadAllAsync()) {
            if (info.State == TaskState.Done)
                return;

            Progress((info.Text, info.Progress));
        }
    }

    public async Task<MemoryStream> ReceiveFilesInline(string RemotePath)
    {
        var session = FilesClient.Internal_SendFiles(new SendFilesInput {
            Paths = { RemotePath }
        });

        var mem = new MemoryStream();

        await foreach (var block in session.ResponseStream.ReadAllAsync()) {
            if (block.DataCase == FileBuffer.DataOneofCase.Buffer) {
                mem.Write(block.Buffer.Span);
            }
        }

        return mem;
    }

    public async Task<Shared.Abstractions.FileSystemItem> GetDirectoryInfo(string Path)
    {
        RTSharp.Daemon.Protocols.FileSystemItem dir;
        try {
            dir = await FilesClient.GetDirectoryInfoAsync(new StringValue { Value = Path });
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to GetDirectoryInfo");
            throw;
        }

        static Shared.Abstractions.FileSystemItem map(RTSharp.Daemon.Protocols.FileSystemItem In)
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
            Log.Logger.Error(ex, "Failed to CreateDirectory");
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
            Log.Logger.Error(ex, "Failed to CheckExists");
            throw;
        }
    }

    public async Task RemoveEmptyDirectory(string Path)
    {
        try {
            await FilesClient.RemoveEmptyDirectoryAsync(new StringValue { Value = Path });
        } catch (Exception ex) {
            Log.Logger.Error(ex, "Failed to RemoveEmptyDirectory");
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
            Log.Logger.Error(ex, "Failed to Mediainfo");
            throw;
        }

        return reply.Output;
    }

    public async Task<Guid> RunCustomScript(string Script, string Name, IReadOnlyList<ScriptMessage> Input)
    {
        var session = await ServerClient.StartScriptAsync(new StartScriptInput {
            Script = Script,
            Name = Name,
            Input = { Input.Select(x => x.ToProtocol()) }
        });

        return new Guid(session.Id.Span);
    }

    public async Task QueueScriptCancellation(Guid Id)
    {
        await ServerClient.StopScriptAsync(new BytesValue { Value = ByteString.CopyFrom(Id.ToByteArray()) });
    }

    public async Task RemoveScriptSession(Guid Id)
    {
        await ServerClient.RemoveScriptAsync(new BytesValue { Value = ByteString.CopyFrom(Id.ToByteArray()) });
    }

    public async Task<Shared.Abstractions.InstalledScript> InstallScript(string Id, string Name, Guid OwnerId, string Script, IReadOnlyList<ScriptMessage> Input)
    {
        return MapInstalledScript(await ServerClient.InstallScriptAsync(new InstallScriptInput {
            Id = Id,
            OwnerId = OwnerId.ToString(),
            Start = new StartScriptInput {
                Script = Script,
                Name = Name,
                Input = { Input.Select(x => x.ToProtocol()) }
            }
        }));
    }

    public async Task UninstallScript(string Id)
    {
        await ServerClient.UninstallScriptAsync(new StringValue { Value = Id });
    }

    public async Task<IReadOnlyList<Shared.Abstractions.InstalledScript>> ListInstalledScripts()
    {
        var reply = await ServerClient.ListInstalledScriptsAsync(new Empty());

        return [ ..reply.Scripts.Select(MapInstalledScript) ];
    }

    public async Task AcknowledgeOrphanedScript(string Id)
    {
        await ServerClient.AcknowledgeOrphanedScriptAsync(new StringValue { Value = Id });
    }

    static Shared.Abstractions.InstalledScript MapInstalledScript(RTSharp.Daemon.Protocols.InstalledScript In)
    {
        return new Shared.Abstractions.InstalledScript {
            Id = In.Id,
            Name = In.Name,
            SessionId = In.SessionId is { Length: 16 } ? new Guid(In.SessionId.Span) : null,
            OwnerId = Guid.TryParse(In.OwnerId, out var ownerId) ? ownerId : Guid.Empty,
            ScriptHash = In.ScriptHash.ToByteArray(),
            OrphanAcknowledged = In.OrphanAcknowledged,
            State = In.State switch {
                TaskState.Done => TASK_STATE.DONE,
                TaskState.Running => TASK_STATE.RUNNING,
                TaskState.Waiting => TASK_STATE.WAITING,
                TaskState.Failed => TASK_STATE.FAILED,
                TaskState.Cancelled => TASK_STATE.CANCELLED,
                _ => throw new ArgumentOutOfRangeException()
            },
            StatusText = In.StatusText,
            LastError = String.IsNullOrEmpty(In.LastError) ? null : In.LastError,
            InstalledAt = In.InstalledAt.ToDateTimeOffset(),
            StartedAt = In.StartedAt?.ToDateTimeOffset()
        };
    }

    ScriptProgressState MapProgressState(RTSharp.Daemon.Protocols.ScriptProgressState In)
    {
        return new ScriptProgressState {
            Id = new Guid(In.Id.ToByteArray()),
            Progress = In.Progress < 0 ? null : In.Progress,
            State = In.State switch {
                TaskState.Done => TASK_STATE.DONE,
                TaskState.Running => TASK_STATE.RUNNING,
                TaskState.Waiting => TASK_STATE.WAITING,
                TaskState.Failed => TASK_STATE.FAILED,
                TaskState.Cancelled => TASK_STATE.CANCELLED,
                _ => throw new ArgumentOutOfRangeException()
            },
            Text = In.Text,
            Chain = In.Chain?.Select(MapProgressState).ToArray() ?? []
        };
    }

    Shared.Abstractions.ScriptSessionUpdate MapScriptSessionUpdate(RTSharp.Daemon.Protocols.ScriptSessionUpdate In)
    {
        return new Shared.Abstractions.ScriptSessionUpdate {
            SessionId = new Guid(In.SessionId.ToByteArray()),
            SessionName = string.IsNullOrWhiteSpace(In.SessionName) ? null : In.SessionName,
            ParentStateId = In.ParentStateId != null ? new Guid(In.ParentStateId.ToByteArray()) : null,
            InstalledScriptId = In.InstalledScriptId,
            State = MapProgressState(In.State)
        };
    }

    public async Task GetScriptProgress(Guid Id, Action<ScriptProgressState>? Progress)
    {
        var cts = new CancellationTokenSource();
        var stream = ServerClient.ScriptStatus(Id.ToByteArray().ToBytesValue(), cancellationToken: cts.Token);
        
        try {
            await foreach (var progressData in stream.ResponseStream.ReadAllAsync()) {
                Progress?.Invoke(MapProgressState(progressData));
                if (progressData.State == TaskState.Done || progressData.State == TaskState.Failed || progressData.State == TaskState.Cancelled) {
                    cts.Cancel();
                }
            }
        } catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled) { }
    }

    public IScriptChannel OpenScriptChannel(Guid SessionId, CancellationToken CancellationToken)
    {
        var call = ServerClient.ScriptChannel(cancellationToken: CancellationToken);

        return new ScriptChannel(call, SessionId, CancellationToken);
    }

    public ChannelReader<Shared.Abstractions.ScriptSessionUpdate> StreamScriptsStatus(CancellationToken CancellationToken, bool IncludeFullUpdate = true, bool StreamUpdates = true)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<Shared.Abstractions.ScriptSessionUpdate>(new UnboundedChannelOptions {
            SingleReader = false,
            SingleWriter = true
        });

        if (!IncludeFullUpdate && !StreamUpdates) {
            channel.Writer.TryComplete();
            return channel.Reader;
        }

        var mode = ScriptSessionsStatusMode.None;
        if (IncludeFullUpdate) {
            mode |= ScriptSessionsStatusMode.FullUpdate;
        }

        if (StreamUpdates) {
            mode |= ScriptSessionsStatusMode.Streaming;
        }

        _ = Task.Run(async () => {
            try {
                var stream = ServerClient.ScriptsStatus(new ScriptSessionsStatusRequest {
                    Mode = mode
                }, cancellationToken: CancellationToken);
                await foreach (var state in stream.ResponseStream.ReadAllAsync(cancellationToken: CancellationToken)) {
                    await channel.Writer.WriteAsync(MapScriptSessionUpdate(state), CancellationToken);
                }
            } finally {
                channel.Writer.TryComplete();
            }
        }, CancellationToken);
        return channel;
    }
    
    public async Task<Dictionary<string, bool>> AllowedToDeleteFiles(IEnumerable<string> In)
    {
        var reply = await FilesClient.AllowedToDeleteAsync(new AllowedToDeleteInput
        {
            Paths = { In }
        });
        
        return reply.Reply.ToDictionary(x => x.Path, x => x.Value);
    }
    
    public async Task<Dictionary<string, bool>> AllowedToReadFiles(IEnumerable<string> In)
    {
        var reply = await FilesClient.AllowedToReadAsync(new AllowedToReadInput
        {
            Paths = { In }
        });
        
        return reply.Reply.ToDictionary(x => x.Path, x => x.Value);
    }

    public async Task<byte[]> HashFileBlock(string Path, ulong Start, ulong End, FileBlockHashAlgorithm HashAlgorithm)
    {
        var input = new HashFileBlockInput
        {
            Path = Path,
            Start = Start,
            End = End
        };

        switch (HashAlgorithm) {
            case FileBlockHashAlgorithm.Sha1:
                input.Algorithm = HashFileBlockInput.Types.HashAlgorithm.Sha1;
                break;
            case FileBlockHashAlgorithm.Sha256BitTorrentV2Merkle Merkle:
                input.Algorithm = HashFileBlockInput.Types.HashAlgorithm.Sha256BtV2Merkle;
                input.MerkleHeight = (uint)Merkle.Height;
                break;
            default:
                throw new NotSupportedException();
        }

        var reply = await FilesClient.HashFileBlockAsync(input);
        return reply.Value.ToByteArray();
    }

    public async Task LinkFiles(List<KeyValuePair<string, string>> SrcToDst, bool Reflink, bool Hardlink)
    {
        await FilesClient.LinkFilesAsync(new LinkFilesInput {
            Source = { SrcToDst.Select(x => x.Key) },
            Target = { SrcToDst.Select(x => x.Value) },
            Type = (Reflink ? LinkFilesInput.Types.LinkType.Reflink : 0) | (Hardlink ? LinkFilesInput.Types.LinkType.Hardlink : 0)
        });
    }

    public T GetGrpcService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : ClientBase<T>
    {
        return (T)Activator.CreateInstance(typeof(T), Channel)!;
    }

    public IDaemonTorrentsService GetTorrentsService(IDataProvider DataProvider) {
        return new DaemonTorrentsService(Plugins.DataProviders.Items.First(x => x.Instance == DataProvider));
    }

    public IDaemonStatsService GetStatsService(IDataProvider DataProvider)
    {
        return new DaemonStatsService(Plugins.DataProviders.Items.First(x => x.Instance == DataProvider));
    }

    public ChannelReader<DataProviderDaemonLogEntry> StreamLogs(IDataProvider DataProvider, int History, CancellationToken CancellationToken)
    {
        var dp = Plugins.DataProviders.Items.First(x => x.Instance == DataProvider);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<DataProviderDaemonLogEntry>(new UnboundedChannelOptions {
            SingleReader = false,
            SingleWriter = true
        });

        _ = Task.Run(async () => {
            try {
                var stream = LogsClient.StreamLogs(new StreamLogsRequest {
                    History = History,
                    Follow = true
                }, headers: dp.GetBuiltInDataProviderGrpcHeaders(), cancellationToken: CancellationToken);

                await foreach (var entry in stream.ResponseStream.ReadAllAsync(CancellationToken)) {
                    await channel.Writer.WriteAsync(new DataProviderDaemonLogEntry(
                        entry.Timestamp.ToDateTime().ToLocalTime(),
                        entry.Severity switch {
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Debug => Shared.Abstractions.LogSeverity.Debug,
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Info => Shared.Abstractions.LogSeverity.Info,
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Notice => Shared.Abstractions.LogSeverity.Notice,
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Warning => Shared.Abstractions.LogSeverity.Warning,
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Error => Shared.Abstractions.LogSeverity.Error,
                            RTSharp.Daemon.Protocols.DataProvider.LogSeverity.Critical => Shared.Abstractions.LogSeverity.Critical,
                            _ => Shared.Abstractions.LogSeverity.Unspecified
                        },
                        entry.Message
                    ), CancellationToken);
                }
            } catch (Exception ex) {
                channel.Writer.TryComplete(ex);
                return;
            } finally {
                channel.Writer.TryComplete();
            }
        }, CancellationToken);

        return channel.Reader;
    }
}
