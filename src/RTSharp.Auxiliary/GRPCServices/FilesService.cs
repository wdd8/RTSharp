using CliWrap;
using CliWrap.Buffered;

using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Microsoft.AspNetCore.Http;

using Mono.Unix;

using RTSharp.Auxiliary.Protocols;

using System.IO;
using System.Threading.Channels;

namespace RTSharp.Auxiliary.Services
{
    public class FilesService(
        ILogger<FilesService> Logger,
        Services.FileTransferService FileTransferService
    ) : GRPCFilesService.GRPCFilesServiceBase
    {
        private static string FileAccessPermissionsToString(UnixFileSystemInfo Input)
        {
            var x = Input.FileAccessPermissions;
            return String.Format("{0}{1}{2}{3}{4}{5}{6}{7}{8}{9}",
                Input.IsDirectory ? "d" : "-",

                x.HasFlag(FileAccessPermissions.UserRead) ? "r" : "-",
                x.HasFlag(FileAccessPermissions.UserWrite) ? "w" : "-",
                x.HasFlag(FileAccessPermissions.UserExecute) ? "x" : "-",

                x.HasFlag(FileAccessPermissions.GroupRead) ? "r" : "-",
                x.HasFlag(FileAccessPermissions.GroupWrite) ? "w" : "-",
                x.HasFlag(FileAccessPermissions.GroupExecute) ? "x" : "-",

                x.HasFlag(FileAccessPermissions.OtherRead) ? "r" : "-",
                x.HasFlag(FileAccessPermissions.OtherWrite) ? "w" : "-",
                x.HasFlag(FileAccessPermissions.OtherExecute) ? "x" : "-"
            );
        }

        private async ValueTask<string> ExpandTilde(string In)
        {
            if (In.StartsWith("~+/"))
                return Environment.CurrentDirectory + "/" + In[3..];
            if (In.StartsWith("~/"))
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "/" + In[2..];
            if (In.StartsWith("~")) {
                var slashIdx = In.IndexOf('/');
                if (slashIdx == -1)
                    return In;

                var uname = In[1..slashIdx];

                var passwd = await File.ReadAllLinesAsync("/etc/passwd");

                foreach (var line in passwd) {
                    var split = line.Split(':');
                    if (split.Length < 6)
                        continue;

                    if (split[0] == uname) {
                        var homeDir = split[5];
                        return homeDir + In[slashIdx..];
                    }
                }

                return In[slashIdx..];
            }

            return In;
        }

        public override Task<CheckExistsReply> CheckExists(CheckExistsInput Req, ServerCallContext Ctx)
        {
            var existence = new Dictionary<string, bool>();
            foreach (var file in Req.Paths) {
                existence.Add(file, File.Exists(file));
            }

            return Task.FromResult(new CheckExistsReply {
                Existence = { existence }
            });
        }

        public override async Task<Empty> CreateDirectory(StringValue Req, ServerCallContext Ctx)
        {
            Logger.LogDebug($"CreateDirectory: {Req.Value}");
            var expanded = await ExpandTilde(Req.Value);
            Logger.LogDebug($"CreateDirectory expanded: {expanded}");

            try {
                Directory.CreateDirectory(expanded);
            } catch (Exception ex) {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
            }

            return new Empty();
        }

        public override async Task<FileSystemItem> GetDirectoryInfo(StringValue Req, ServerCallContext Ctx)
        {
            Logger.LogDebug($"GetDirectoryInfo: {Req.Value}");
            var expanded = await ExpandTilde(Req.Value);
            Logger.LogDebug($"GetDirectoryInfo expanded: {expanded}");

            if (!UnixFileSystemInfo.TryGetFileSystemEntry(expanded, out var info))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Failed to open directory"));

            var ret = new FileSystemItem() {
                Path = info.FullName,
                Directory = info.IsDirectory,
                Permissions = FileAccessPermissionsToString(info),
                Size = (ulong)info.Length,
                LastModified = Timestamp.FromDateTime(info.LastWriteTimeUtc)
            };

            if (info.IsDirectory) {
                var entriesRaw = Directory.GetFileSystemEntries(info.FullName);

                var entries = entriesRaw.Select(x => {
                    if (UnixFileSystemInfo.TryGetFileSystemEntry(x, out var entry)) {
                        return entry;
                    }

                    return null;
                });

                ret.Children.AddRange(entries.Where(x => x != null).Select(x => new FileSystemItem() {
                    Path = x.FullName,
                    Directory = x.IsDirectory,
                    Children = { },
                    LastModified = Timestamp.FromDateTime(x.LastWriteTimeUtc),
                    Permissions = FileAccessPermissionsToString(x),
                    Size = (ulong)x.Length
                }));
            }

            return ret;
        }

        public override async Task<MediaInfoReply> MediaInfo(MediaInfoInput Req, ServerCallContext Ctx)
        {
            try {
                var mi = await Cli.Wrap("mediainfo").WithArguments("--Version").WithValidation(CommandResultValidation.None).ExecuteBufferedAsync(Ctx.CancellationToken);
                if (mi.ExitCode != 0) {
                    throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, "mediainfo is not installed"));
                }
            } catch {
                throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, "mediainfo is not installed"));
            }

            var outp = await Cli.Wrap("mediainfo").WithArguments(Req.Paths).WithValidation(CommandResultValidation.None).ExecuteBufferedAsync(Ctx.CancellationToken);
            if (outp.ExitCode != 0)
                throw new RpcException(new Grpc.Core.Status(StatusCode.FailedPrecondition, "mediainfo failed"));

            var split = outp.StandardOutput.Split("General\n", StringSplitOptions.RemoveEmptyEntries).Select(x => "General\n" + x);

            return new MediaInfoReply() {
                Output = { split }
            };
        }

        public override async Task<Empty> RemoveEmptyDirectory(StringValue Req, ServerCallContext Ctx)
        {
            Logger.LogDebug($"RemoveEmptyDirectory: {Req.Value}");
            var expanded = await ExpandTilde(Req.Value);
            Logger.LogDebug($"RemoveEmptyDirectory expanded: {expanded}");

            try {
                Directory.Delete(expanded);
            } catch (Exception ex) {
                throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
            }

            return new Empty();
        }

        public override Task<Empty> CancelFileTransfer(FileTransferSession Req, ServerCallContext Ctx)
        {
            throw new NotImplementedException();
        }

        public override async Task<FileTransferSession> ReceiveFilesFromRemote(ReceiveFilesFromRemoteInput Req, ServerCallContext Ctx)
        {
            Logger.LogInformation($"ReceiveFilesFromRemote: target {Req.TargetUrl}");

            var session = await FileTransferService.ReceiveFilesFromRemote(Req.TargetUrl, Req.StorePaths.Select((x, i) => (StorePath: x, RemoteSourcePath: Req.RemoteSourcePaths[i])));
            return new FileTransferSession {
                SessionId = session.Id
            };
        }

        public override Task ReceiveFiles(ReceiveFilesInput Req, IServerStreamWriter<FileBuffer> Res, ServerCallContext Ctx)
        {
            Logger.LogInformation($"ReceiveFile: {Req.SessionId}");

            var session = FileTransferService.GetOrCreateSession(Req.SessionId, Ctx.Peer);

            if (Req.Paths.Any(x => !File.Exists(x))) {
                throw new ArgumentException("File doesn't exist");
            }

            var streams = Req.Paths.ToDictionary(x => x, x => {
                try {
                    return File.OpenRead(x);
                } catch (UnauthorizedAccessException) {
                    throw;
                }
            });

            async Task transfer()
            {
                try {
                    foreach (var (path, file) in streams) {
                        await Res.WriteAsync(new FileBuffer {
                            Path = path
                        });
                        await FileTransferService.SendFile(session, path, file, Res);
                    }
                    await Res.WriteAsync(new FileBuffer {
                        Path = ""
                    });
                } catch (Exception ex) {
                    Logger.LogError(ex, "ReceiveFiles: transfer task failed");
                }
            }

            return session.Transfer = transfer();
        }

        public override async Task FileTransferSessionsProgress(Empty _, IServerStreamWriter<FileTransferSessionProgress> Res, ServerCallContext Ctx)
        {
            var channel = Channel.CreateBounded<FileTransferSessionProgress>(new BoundedChannelOptions(10) {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = true
            });

            var progress = new Progress<FileTransferService.FileTransferSessionProgress>(progress => {
                channel.Writer.TryWrite(new FileTransferSessionProgress {
                    SessionId = progress.Session.Id,
                    Sender = progress.Session.Sender,
                    TargetUrl = progress.Session.ConnectedServer,
                    File = progress.Path,
                    BytesReceived = (ulong)progress.BytesReceived
                });
            });

            FileTransferService.SubscribeToAllSessions(progress);

            Ctx.CancellationToken.Register(() => {
                FileTransferService.Unsubscribe(progress);
                channel.Writer.Complete();
            });

            await foreach (var info in channel.Reader.ReadAllAsync()) {
                Logger.LogInformation($"Progress: {info.File} {info.SessionId} {info.BytesReceived}");
                await Res.WriteAsync(info);
            }
        }
    }
}