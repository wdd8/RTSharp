using Google.Protobuf;

using Grpc.Core;
using Grpc.Net.Client;

using Microsoft.Extensions.Caching.Memory;

using RTSharp.Daemon.Protocols;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace RTSharp.Daemon.Services
{
    public class FileTransferService
    {
        public readonly record struct FileTransferSessionProgress(string Path, long BytesReceived);

        public ILogger<FileTransferService> Logger { get; }

        private readonly Dictionary<string, GrpcChannel> Channels = new();

        public FileTransferService(ILogger<FileTransferService> Logger, IConfiguration config)
        {
            this.Logger = Logger;
            this.Config = config;
        }

        private readonly IConfiguration Config;


        public async Task ReceiveFilesFromRemote(Protocols.GRPCFilesService.GRPCFilesServiceClient Client, IEnumerable<(string StorePath, string RemoteSourcePath)> Paths, Action<FileTransferSessionProgress> Progress)
        {
            foreach (var dir in Paths.Select(x => Path.GetDirectoryName(x.StorePath)).Distinct())
                Directory.CreateDirectory(dir!); // possible that its null but thats consumers problem

            Logger.LogInformation($"ReceiveFilesFromRemote: Request SendFile");

            var paths = Paths.ToDictionary(x => x.RemoteSourcePath, x => x.StorePath);

            var req = Client.Internal_SendFiles(new SendFilesInput {
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
                        try { Progress(new("", bytesTotal)); } catch { }
                        return;
                    }

                    Logger.LogDebug($"ReceiveFilesFromRemote: file {buffer.Path} -> {paths[buffer.Path]}");
                    file = File.OpenWrite(currentPath = paths[buffer.Path]);
                    bytesTotal = 0;

                    try { Progress(new(currentPath!, bytesTotal)); } catch { }

                    continue;
                } else if (buffer.DataCase == FileBuffer.DataOneofCase.Buffer) {
                    await file!.WriteAsync(buffer.Buffer.Memory);
                    bytesTotal += buffer.Buffer.Length;

                    if (sw.Elapsed > TimeSpan.FromSeconds(1)) {
                        try { Progress(new(currentPath!, bytesTotal)); } catch { }
                        sw.Restart();
                    }
                }
            }
        }

        public async Task SendFile(string Path, FileStream file, IServerStreamWriter<FileBuffer> Stream)
        {
            Memory<byte> buffer = new byte[16384];

            long bytesRead, bytesTotal = 0;
            //var sw = Stopwatch.StartNew();

            //var outpBuffer = new FileBuffer();

            Logger.LogDebug($"SendFile: {Path}, len {file.Length}");

            while ((bytesRead = await file.ReadAsync(buffer)) > 0) {
                /*var bytes = UnsafeByteOperations.UnsafeWrap(buffer[..(int)bytesRead]);
                outpBuffer.Buffer = bytes;*/
                //await Stream.WriteAsync(outpBuffer);
                await Stream.WriteAsync(new FileBuffer {
                    Buffer = ByteString.CopyFrom(buffer[..(int)bytesRead].Span)
                });

                bytesTotal += bytesRead;
                /*if (sw.Elapsed > TimeSpan.FromMilliseconds(500)) {
                    sw.Restart();
                }*/
            }
        }
    }
}
