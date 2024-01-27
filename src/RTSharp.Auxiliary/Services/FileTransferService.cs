using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Caching.Memory;

using RTSharp.Auxiliary.Protocols;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace RTSharp.Auxiliary.Services
{
    public class FileTransferService
    {
        public record FileTransferSession
        {
            public required string Id { get; init; }

            public required string ConnectedServer { get; init; }

            public required bool Sender { get; init; }

            public Task Transfer { get; set; }
        }

        public readonly record struct FileTransferSessionProgress(FileTransferSession Session, string Path, long BytesReceived);

        private static ConcurrentDictionary<string, FileTransferSession> Sessions { get; } = new();
        private static List<IProgress<FileTransferSessionProgress>> Progress { get; } = new();
        private static ReaderWriterLockSlim ProgressListLock = new();

        public ILogger<FileTransferService> Logger { get; }

        private readonly Dictionary<string, GrpcChannel> Channels = new();

        public FileTransferService(ILogger<FileTransferService> Logger, IConfiguration config)
        {
            this.Logger = Logger;
            this.Config = config;
        }

        private async ValueTask<GrpcChannel> GetChannel(string Url)
        {
            if (Channels.TryGetValue(Url, out var ret))
                return ret;

            var publicPem = await System.IO.File.ReadAllTextAsync(Config.GetSection("Certificate").GetValue<string>("PublicPem"));
            var privatePem = await System.IO.File.ReadAllTextAsync(Config.GetSection("Certificate").GetValue<string>("PrivatePem"));
            var x509 = X509Certificate2.CreateFromPem(publicPem, privatePem);
            var cert = new X509Certificate2(x509.Export(X509ContentType.Pkcs12));
            var allowedClients = Config.GetSection("AllowedClients").Get<string[]>();

            Logger.LogInformation($"Creating channel with certificate {cert.GetCertHashString(HashAlgorithmName.SHA256)}");

            return Channels[Url] = GrpcChannel.ForAddress(Url, new GrpcChannelOptions() {
                HttpHandler = new SocketsHttpHandler() {
                    SslOptions = new System.Net.Security.SslClientAuthenticationOptions {
                        ClientCertificates = [ cert ],
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

        internal static readonly char[] chars =
            "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890_-".ToCharArray();
        private readonly IConfiguration Config;

        public string RandomString()
        {
            const int size = 64;
            byte[] data = new byte[4 * size];
            using (var rng = RandomNumberGenerator.Create()) {
                rng.GetBytes(data);
            }
            StringBuilder result = new StringBuilder(size);
            for (int i = 0;i < size;i++) {
                var rnd = BitConverter.ToUInt32(data, i * 4);
                var idx = rnd % chars.Length;

                result.Append(chars[idx]);
            }

            return result.ToString();
        }

        public async ValueTask<FileTransferSession> ReceiveFilesFromRemote(string Url, IEnumerable<(string StorePath, string RemoteSourcePath)> Paths)
        {
            foreach (var dir in Paths.Select(x => Path.GetDirectoryName(x.StorePath)).Distinct())
                Directory.CreateDirectory(dir!); // possible that its null but thats consumers problem

            var id = RandomString();
            var session = Sessions[id] = new() {
                Id = id,
                ConnectedServer = Url,
                Sender = false
            };

            var channel = await GetChannel(Url);
            var grpc = new Protocols.GRPCFilesService.GRPCFilesServiceClient(channel);

            async Task task()
            {
                Logger.LogInformation($"ReceiveFilesFromRemote: Request SendFile session {session.Id}");

                var paths = Paths.ToDictionary(x => x.RemoteSourcePath, x => x.StorePath);

                var req = grpc.ReceiveFiles(new Protocols.ReceiveFilesInput {
                    SessionId = session.Id,
                    Paths = { Paths.Select(x => x.RemoteSourcePath) }
                });

                FileStream file = null;
                string currentPath = null;
                var sw = Stopwatch.StartNew();
                long bytesTotal = 0;
                await foreach (var buffer in req.ResponseStream.ReadAllAsync()) {
                    if (buffer.DataCase == Protocols.FileBuffer.DataOneofCase.Path) {
                        if (file != null)
                            await file.FlushAsync();

                        if (buffer.Path == "") {
                            // Done
                            Logger.LogInformation($"ReceiveFilesFromRemote: Done");
                            ProgressListLock.EnterReadLock(); {
                                foreach (var progress in Progress) {
                                    progress.Report(new(session, "", bytesTotal));
                                }
                            } ProgressListLock.ExitReadLock();
                            return;
                        }

                        Logger.LogDebug($"ReceiveFilesFromRemote: file {buffer.Path} -> {paths[buffer.Path]}");
                        file = File.OpenWrite(currentPath = paths[buffer.Path]);
                        bytesTotal = 0;

                        ProgressListLock.EnterReadLock(); {
                            foreach (var progress in Progress) {
                                progress.Report(new(session, currentPath!, bytesTotal));
                            }
                        } ProgressListLock.ExitReadLock();

                        continue;
                    } else if (buffer.DataCase == FileBuffer.DataOneofCase.Buffer) {
                        await file!.WriteAsync(buffer.Buffer.Memory);
                        bytesTotal += buffer.Buffer.Length;

                        if (sw.Elapsed > TimeSpan.FromSeconds(1)) {
                            ProgressListLock.EnterReadLock(); {
                                foreach (var progress in Progress) {
                                    progress.Report(new(session, currentPath!, bytesTotal));
                                }
                            } ProgressListLock.ExitReadLock();
                            sw.Restart();
                        }
                    }
                }
            }
            session.Transfer = task();

            return session;
        }

        public FileTransferSession GetOrCreateSession(string SessionId, string ConnectedServer)
        {
            if (Sessions.TryGetValue(SessionId, out var ret))
                return ret;

            return Sessions[SessionId] = new FileTransferSession {
                Id = SessionId,
                ConnectedServer = ConnectedServer,
                Sender = true
            };
        }

        public async Task SendFile(FileTransferSession Session, string Path, FileStream file, IServerStreamWriter<FileBuffer> Stream)
        {
            Memory<byte> buffer = new byte[16384];

            long bytesRead, bytesTotal = 0;
            var sw = Stopwatch.StartNew();

            var outpBuffer = new FileBuffer();

            Logger.LogDebug($"SendFile: {Path}, len {file.Length}");

            while ((bytesRead = await file.ReadAsync(buffer)) > 0) {
                /*var bytes = UnsafeByteOperations.UnsafeWrap(buffer[..(int)bytesRead]);
                outpBuffer.Buffer = bytes;*/
                //await Stream.WriteAsync(outpBuffer);
                await Stream.WriteAsync(new FileBuffer {
                    Buffer = ByteString.CopyFrom(buffer[..(int)bytesRead].Span)
                });

                bytesTotal += bytesRead;
                if (sw.Elapsed > TimeSpan.FromMilliseconds(500)) {
                    ProgressListLock.EnterReadLock(); {
                        foreach (var progress in Progress) {
                            progress.Report(new(Session, Path, bytesTotal));
                        }
                    } ProgressListLock.ExitReadLock();
                    sw.Restart();
                }
            }
            ProgressListLock.EnterReadLock(); {
                foreach (var progress in Progress) {
                    progress.Report(new(Session, Path, bytesTotal));
                }
            } ProgressListLock.ExitReadLock();
        }

        public void SubscribeToAllSessions(Progress<FileTransferSessionProgress> In)
        {
            ProgressListLock.EnterWriteLock(); {
                Progress.Add(In);
            } ProgressListLock.ExitWriteLock();
        }

        public void Unsubscribe(Progress<FileTransferSessionProgress> In)
        {
            ProgressListLock.EnterWriteLock(); {
                Progress.Remove(In);
            } ProgressListLock.ExitWriteLock();
        }
    }
}
