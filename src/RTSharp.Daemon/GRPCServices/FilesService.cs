using CliWrap;
using CliWrap.Buffered;

using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using Mono.Unix;
using Mono.Unix.Native;

using RTSharp.Daemon.Protocols;

using System.Runtime.InteropServices;

using Path = System.IO.Path;

namespace RTSharp.Daemon.GRPCServices
{
    public partial class FilesService(
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

        public override async Task Internal_SendFiles(SendFilesInput Req, IServerStreamWriter<FileBuffer> Res, ServerCallContext Ctx)
        {
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

            try {
                foreach (var (path, file) in streams) {
                    Logger.LogInformation($"Sending file {path}...");
                    await Res.WriteAsync(new FileBuffer {
                        Path = path
                    });
                    await FileTransferService.SendFile(path, file, Res);
                }
                Logger.LogInformation($"File send done");
                await Res.WriteAsync(new FileBuffer {
                    Path = ""
                });
            } catch (Exception ex) {
                Logger.LogError(ex, "Internal_SendFiles: transfer task failed");
            }
        }

        public override Task<AllowedToDeleteReply> AllowedToDelete(AllowedToDeleteInput Req, ServerCallContext context)
        {
            var ret = new List<AllowedToDeleteReply.Types.PathWithStatus>();
            
            var uid = Syscall.getuid();
            var gid = Syscall.getgid();
            
            foreach (var file in Req.Paths) {
                if (!File.Exists(file)) {
                    ret.Add(new AllowedToDeleteReply.Types.PathWithStatus {
                        Path = file,
                        Value = false
                    });
                    
                    continue;
                }
                
                var dir = Path.GetDirectoryName(file);
                
                if (!UnixFileSystemInfo.TryGetFileSystemEntry(dir, out var info)) {
                    ret.Add(new AllowedToDeleteReply.Types.PathWithStatus {
                        Path = file,
                        Value = false
                    });
                    
                    continue;
                }
                
                if (info.IsSticky) {
                    ret.Add(new AllowedToDeleteReply.Types.PathWithStatus {
                        Path = file,
                        Value = false
                    });
                    
                    continue;
                }
                
                var userAllowed = (info.FileAccessPermissions & (FileAccessPermissions.UserExecute | FileAccessPermissions.UserWrite)) != 0 && info.OwnerUserId == uid;
                var groupAllowed = (info.FileAccessPermissions & (FileAccessPermissions.GroupExecute | FileAccessPermissions.GroupWrite)) != 0 && info.OwnerGroupId == gid;
                var otherAllowed = (info.FileAccessPermissions & (FileAccessPermissions.OtherExecute | FileAccessPermissions.OtherWrite)) != 0;
                
                ret.Add(new AllowedToDeleteReply.Types.PathWithStatus {
                    Path = file,
                    Value = userAllowed || groupAllowed || otherAllowed
                });
            }
            
            return Task.FromResult(new AllowedToDeleteReply
            {
                Reply = { ret }
            });
        }
        
        public override Task<AllowedToReadReply> AllowedToRead(AllowedToReadInput Req, ServerCallContext context)
        {
            var ret = new List<AllowedToReadReply.Types.PathWithStatus>();
            
            var uid = Syscall.getuid();
            var gid = Syscall.getgid();
            
            foreach (var file in Req.Paths) {
                if (!File.Exists(file)) {
                    ret.Add(new AllowedToReadReply.Types.PathWithStatus {
                        Path = file,
                        Value = false
                    });
                    
                    continue;
                }
                
                if (!UnixFileSystemInfo.TryGetFileSystemEntry(file, out var info)) {
                    ret.Add(new AllowedToReadReply.Types.PathWithStatus {
                        Path = file,
                        Value = false
                    });
                    
                    continue;
                }
                
                var userAllowed = (info.FileAccessPermissions & FileAccessPermissions.UserRead) != 0 && info.OwnerUserId == uid;
                var groupAllowed = (info.FileAccessPermissions & FileAccessPermissions.GroupRead) != 0 && info.OwnerGroupId == gid;
                var otherAllowed = (info.FileAccessPermissions & FileAccessPermissions.OtherRead) != 0;
                
                ret.Add(new AllowedToReadReply.Types.PathWithStatus {
                    Path = file,
                    Value = userAllowed || groupAllowed || otherAllowed
                });
            }
            
            return Task.FromResult(new AllowedToReadReply
            {
                Reply = { ret }
            });
        }

        public override async Task<BytesValue> HashFileBlock(HashFileBlockInput Req, ServerCallContext Ctx)
        {
            if (!File.Exists(Req.Path))
                throw new RpcException(new Status(StatusCode.InvalidArgument, "File doesn't exist"));

            if (Req.End - Req.Start > 32 * 1024 * 1024)
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Requested block is too large"));

            Memory<byte> buffer;

            try {
                using var stream = File.OpenRead(Req.Path);
                stream.Seek((long)Req.Start, SeekOrigin.Begin);
                buffer = new Memory<byte>(new byte[Req.End - Req.Start]);

                await stream.ReadExactlyAsync(buffer);
            } catch (Exception ex) {
                throw new RpcException(new Status(StatusCode.Internal, $"Failed to read file block: {ex.Message}"));
            }

            var hash = Req.Algorithm switch {
                HashFileBlockInput.Types.HashAlgorithm.Sha1 => System.Security.Cryptography.SHA1.HashData(buffer.Span),
                HashFileBlockInput.Types.HashAlgorithm.Sha256 => System.Security.Cryptography.SHA256.HashData(buffer.Span),
                _ => throw new RpcException(new Status(StatusCode.InvalidArgument, "Unsupported hash algorithm")),
            };
            return new BytesValue {
                Value = ByteString.CopyFrom(hash)
            };
        }

        /*[LibraryImport("libc")]
        private static unsafe partial nint copy_file_range(nint fd_in, nint* off_in, nint fd_out, nint* off_out, nint size, nuint flags);*/

        [LibraryImport("libc")]
        private static unsafe partial nint ioctl(nint dest_fd, nint op, nint src_fd);

        public override async Task<Empty> LinkFiles(LinkFilesInput Req, ServerCallContext Ctx)
        {
            if (Req.Source.Count != Req.Target.Count) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Source and Target path count must match"));
            }

            foreach (var (src, dst) in Req.Source.Zip(Req.Target)) {
                int hSrcFile = -1;
                int hDstFile = -1;
                bool created = false;

                try {
                    if (Req.Type.HasFlag(LinkFilesInput.Types.LinkType.Reflink)) {
                        try {
                            hSrcFile = Syscall.open(src, OpenFlags.O_RDONLY, 0);
                            if (hSrcFile == -1) {
                                var errno = Syscall.GetLastError();
                                if (errno == Errno.EACCES || errno == Errno.EPERM) {
                                    throw new RpcException(new Status(StatusCode.PermissionDenied, $"Cannot open {src}"));
                                } else if (errno == Errno.ENOENT) {
                                    throw new RpcException(new Status(StatusCode.InvalidArgument, $"{dst} doesn't exist"));
                                } else {
                                    throw new RpcException(new Status(StatusCode.Internal, $"open {dst}: {errno}"));
                                }
                            }
                        } catch (Exception ex) {
                            throw new RpcException(new Status(StatusCode.Internal, "Error: " + ex.Message, ex));
                        }

                        if (Syscall.fstat(hSrcFile, out var stat) != 0) {
                            var errno = Syscall.GetLastError();
                            if (errno == Errno.EACCES || errno == Errno.EPERM) {
                                throw new RpcException(new Status(StatusCode.PermissionDenied, $"Cannot stat {src}"));
                            } else if (errno == Errno.ENOENT) {
                                throw new RpcException(new Status(StatusCode.InvalidArgument, $"{dst} doesn't exist"));
                            } else {
                                throw new RpcException(new Status(StatusCode.Internal, $"stat {dst}: {errno}"));
                            }
                        }

                        try {
                            for (var x = 0;x < 2;x++) {
                                hDstFile = Syscall.open(dst, OpenFlags.O_CREAT | OpenFlags.O_WRONLY, stat.st_mode);
                                if (hDstFile == -1) {
                                    var errno = Syscall.GetLastError();
                                    if (errno == Errno.ENOENT) {
                                        Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                                    } else if (errno == Errno.EACCES || errno == Errno.EPERM) {
                                        throw new RpcException(new Status(StatusCode.PermissionDenied, $"Cannot open {dst}"));
                                    } else if (errno == Errno.EEXIST) {
                                        throw new RpcException(new Status(StatusCode.InvalidArgument, $"{dst} already exists"));
                                    } else {
                                        throw new RpcException(new Status(StatusCode.Internal, $"open {dst}: {errno}"));
                                    }
                                } else
                                    break;
                            }
                        } catch (Exception ex) {
                            throw new RpcException(new Status(StatusCode.Internal, "Error (2): " + ex.Message, ex));
                        }

                        var resp = ioctl(hDstFile, 0x40049409 /* FICLONE */, hSrcFile);
                        if (resp != 0) {
                            var errno = Syscall.GetLastError();
                            if (errno != Errno.EOPNOTSUPP) {
                                throw new RpcException(new Status(StatusCode.Internal, "ioctl error: " + errno));
                            }
                        } else
                            created = true;
                    }

                    if (Req.Type.HasFlag(LinkFilesInput.Types.LinkType.Hardlink) && !created) {
                        var resp = Syscall.link(src, dst);
                        if (resp != 0) {
                            var errno = Syscall.GetLastError();
                            if (errno == Errno.EACCES || errno == Errno.EPERM) {
                                throw new RpcException(new Status(StatusCode.PermissionDenied, $"Cannot open {dst}"));
                            } else if (errno == Errno.EEXIST) {
                                throw new RpcException(new Status(StatusCode.InvalidArgument, $"{dst} already exists"));
                            } else {
                                throw new RpcException(new Status(StatusCode.Internal, $"{dst}: {errno}"));
                            }
                        }
                    }
                } finally {
                    if (hSrcFile != -1)
                        _ = Syscall.close(hSrcFile);
                    if (hDstFile != -1)
                        _ = Syscall.close(hDstFile);
                }
            }
            return new();
        }
    }
}