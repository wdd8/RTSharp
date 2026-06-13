using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Threading;

using Grpc.Core;

using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using RTSharp.Daemon.Protocols;
using RTSharp.Models;
using RTSharp.Shared.Abstractions.Daemon;

using Serilog;

namespace RTSharp.Core.Services;

public class MediaPreviewService
{
    private record PreviewSession(IDaemonService DaemonService, string RemotePath, ulong FileSize, string ContentType);

    private readonly ConcurrentDictionary<string, PreviewSession> _sessions = new();

    private static IPAddress PickLoopbackAddress()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return IPAddress.Loopback;

        Span<byte> rnd = stackalloc byte[3];
        RandomNumberGenerator.Fill(rnd);
        return new IPAddress([127, ..rnd]);
    }

    private static async Task<IConnectionListener> BindAsync(CancellationToken ct)
    {
        var address = PickLoopbackAddress();
        var factory = new SocketTransportFactory(
            Options.Create(new SocketTransportOptions()),
            NullLoggerFactory.Instance
        );
        var listener = await factory.BindAsync(new IPEndPoint(address, 0), ct);
        var ep = (IPEndPoint)listener.EndPoint;
        Log.Logger.Information("MediaPreviewService: listening on http://{Address}:{Port}", ep.Address, ep.Port);
        return listener;
    }

    public async Task OpenPreview(Torrent Torrent, string remotePath, ulong fileSize)
    {
        using var cts = new CancellationTokenSource();
        var listener = await BindAsync(cts.Token);

        var ep = (IPEndPoint)listener.EndPoint;
        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new PreviewSession(Torrent.DataOwner.PluginInstance.AttachedDaemonService, remotePath, fileSize, GetContentType(remotePath));

        var url = $"http://{ep.Address}:{ep.Port}/{token}";
        Log.Logger.Information("MediaPreviewService: preview {Url}", url);

        var listenerTask = Task.Run(() => AcceptLoopAsync(listener, cts.Token));

        try {
            var mpvStartInfo = new ProcessStartInfo("mpv") { UseShellExecute = false };
            mpvStartInfo.ArgumentList.Add("--demuxer-lavf-o=reconnect_on_network_error=0");
            mpvStartInfo.ArgumentList.Add("--demuxer-lavf-o=reconnect_streamed=0");
            mpvStartInfo.ArgumentList.Add("--demuxer-lavf-o=reconnect=0");
            mpvStartInfo.ArgumentList.Add(url);
            using var mpv = new Process { StartInfo = mpvStartInfo };
            mpv.Start();
            await mpv.WaitForExitAsync();
        } catch (Win32Exception ex) when (ex.NativeErrorCode == 2) {
            await Dispatcher.UIThread.InvokeAsync(() =>
                MessageBoxManager.GetMessageBoxStandard(
                    title: "mpv not found",
                    text: "mpv is not installed or not found in PATH.\n\nPlease install mpv to use the preview feature.",
                    @enum: ButtonEnum.Ok,
                    icon: Icon.Error
                ).ShowAsync()
            );
        } catch (Exception ex) {
            Log.Logger.Error(ex, "MediaPreviewService: failed to launch mpv for {Path}", remotePath);
        } finally {
            _sessions.TryRemove(token, out _);
            cts.Cancel();
            await listener.DisposeAsync();
            await listenerTask;
        }
    }

    private async Task AcceptLoopAsync(IConnectionListener listener, CancellationToken ct)
    {
        while (true) {
            ConnectionContext? conn;
            try {
                conn = await listener.AcceptAsync(ct);
            } catch (OperationCanceledException) {
                break;
            }
            if (conn == null) break;
            _ = Task.Run(() => HandleConnectionAsync(conn));
        }
    }

    private async Task HandleConnectionAsync(ConnectionContext connection)
    {
        await using var _ = connection;
        var ct = connection.ConnectionClosed;
        try {
            var inputStream = connection.Transport.Input.AsStream();
            var output = connection.Transport.Output;

            using var reader = new StreamReader(inputStream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(ct) ?? "";
            var parts = requestLine.Split(' ', 3);
            if (parts.Length < 2) return;
            var method = parts[0];
            var path = parts[1];

            string? rangeHeader = null;
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null && line.Length > 0) {
                if (line.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    rangeHeader = line.Substring(6).Trim();
            }

            var token = path.TrimStart('/').Split('/')[0];

            if (!_sessions.TryGetValue(token, out var session)) {
                await WriteResponse(output, "404 Not Found", "", 0, null, ct);
                return;
            }

            if (method == "HEAD") {
                await WriteResponse(output, "200 OK", session.ContentType, (long)session.FileSize, null, ct);
                return;
            }

            if (method != "GET") {
                await WriteResponse(output, "405 Method Not Allowed", "", 0, null, ct);
                return;
            }

            ulong startByte = 0;
            ulong? requestedEndByte = null;
            bool isRangeRequest = false;

            if (rangeHeader != null && rangeHeader.StartsWith("bytes=") && !rangeHeader.Contains(',')) {
                var spec = rangeHeader.AsSpan(6);
                var dash = spec.IndexOf('-');
                if (dash >= 0) {
                    var fromSpan = spec[..dash];
                    var toSpan = spec[(dash + 1)..];
                    if (fromSpan.IsEmpty && ulong.TryParse(toSpan, out var suffixLen)) {
                        startByte = session.FileSize > suffixLen ? session.FileSize - suffixLen : 0;
                        isRangeRequest = true;
                    } else if (ulong.TryParse(fromSpan, out startByte)) {
                        if (!toSpan.IsEmpty && ulong.TryParse(toSpan, out var to))
                            requestedEndByte = to + 1;
                        isRangeRequest = true;
                    }
                }
            }

            if (startByte >= session.FileSize) {
                await WriteResponse(output, "416 Range Not Satisfiable", "", 0, $"bytes */{session.FileSize}", ct);
                return;
            }

            var bytesToSend = requestedEndByte.HasValue ? requestedEndByte.Value - startByte : session.FileSize - startByte;
            string? contentRange = null;
            string status;
            if (isRangeRequest) {
                status = "206 Partial Content";
                var endInclusive = requestedEndByte.HasValue ? requestedEndByte.Value - 1 : session.FileSize - 1;
                contentRange = $"bytes {startByte}-{endInclusive}/{session.FileSize}";
            } else {
                status = "200 OK";
            }

            await WriteResponse(output, status, session.ContentType, (long)bytesToSend, contentRange, ct);

            var filesClient = session.DaemonService.GetGrpcService<GRPCFilesService.GRPCFilesServiceClient>();
            var grpcReq = new SendFilesInput {
                Paths = { session.RemotePath },
                StartByte = startByte
            };
            if (requestedEndByte.HasValue)
                grpcReq.EndByte = requestedEndByte.Value;

            var sendFiles = filesClient.Internal_SendFiles(grpcReq, cancellationToken: ct);
            await foreach (var chunk in sendFiles.ResponseStream.ReadAllAsync(ct)) {
                if (chunk.DataCase == FileBuffer.DataOneofCase.Buffer)
                    await output.WriteAsync(chunk.Buffer.Memory, ct);
            }
            await output.FlushAsync(ct);
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            Log.Logger.Error(ex, "MediaPreviewService: error handling connection");
        }
    }

    private static async Task WriteResponse(System.IO.Pipelines.PipeWriter output, string status, string contentType, long contentLength, string? contentRange, CancellationToken ct)
    {
        var sb = new StringBuilder();
        sb.Append($"HTTP/1.1 {status}\r\n");
        sb.Append($"Accept-Ranges: bytes\r\n");
        if (contentType.Length > 0) sb.Append($"Content-Type: {contentType}\r\n");
        sb.Append($"Content-Length: {contentLength}\r\n");
        if (contentRange != null) sb.Append($"Content-Range: {contentRange}\r\n");
        sb.Append("Connection: close\r\n\r\n");
        await output.WriteAsync(Encoding.ASCII.GetBytes(sb.ToString()), ct);
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            ".mp3" => "audio/mpeg",
            ".flac" => "audio/flac",
            ".m4a" or ".aac" => "audio/mp4",
            ".ogg" or ".ogv" => "video/ogg",
            ".ts" => "video/mp2t",
            ".wmv" => "video/x-ms-wmv",
            _ => "application/octet-stream"
        };
}
