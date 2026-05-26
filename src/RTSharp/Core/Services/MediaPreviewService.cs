using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

using Grpc.Core;

using RTSharp.Daemon.Protocols;
using RTSharp.Shared.Abstractions.Daemon;

using Serilog;

using Avalonia.Threading;

using MsBox.Avalonia;
using MsBox.Avalonia.Enums;

using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using RTSharp.Models;

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

    private async Task<WebApplication> StartKestrelAsync()
    {
        var address = PickLoopbackAddress();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(o => o.Listen(address, 0));

        var app = builder.Build();

        app.Run(HandleRequestAsync);

        await app.StartAsync();

        Log.Logger.Information("MediaPreviewService: listening on {Url}", app.Urls.FirstOrDefault());
        return app;
    }

    public async Task OpenPreview(Torrent Torrent, string remotePath, ulong fileSize)
    {
        await using var webApp = await StartKestrelAsync();
        var baseUrl = webApp.Urls.First();

        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = new PreviewSession(Torrent.DataOwner.PluginInstance.AttachedDaemonService, remotePath, fileSize, GetContentType(remotePath));

        var url = $"{baseUrl}/{token}";
        Log.Logger.Information("MediaPreviewService: preview {Url}", url);

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
            // NativeErrorCode 2 = ERROR_FILE_NOT_FOUND (Windows) / ENOENT (Linux/macOS)

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
            await webApp.StopAsync();
        }
    }

    private async Task HandleRequestAsync(HttpContext ctx)
    {
        var token = ctx.Request.Path.Value?.TrimStart('/').Split('/')[0] ?? "";

        if (!_sessions.TryGetValue(token, out var session)) {
            ctx.Response.StatusCode = 404;
            return;
        }

        ctx.Response.Headers.AcceptRanges = "bytes";

        if (HttpMethods.IsHead(ctx.Request.Method)) {
            ctx.Response.ContentType = session.ContentType;
            ctx.Response.ContentLength = (long)session.FileSize;
            return;
        }

        if (!HttpMethods.IsGet(ctx.Request.Method)) {
            ctx.Response.StatusCode = 405;
            return;
        }

        ulong startByte = 0;
        ulong? requestedEndByte = null;

        var rangeHeader = ctx.Request.GetTypedHeaders().Range;
        bool isRangeRequest = rangeHeader?.Ranges.Count == 1;

        if (isRangeRequest) {
            var range = rangeHeader!.Ranges.First();
            if (range.From.HasValue) {
                startByte = (ulong)range.From.Value;
                requestedEndByte = range.To.HasValue ? (ulong)(range.To.Value + 1) : null;
            } else if (range.To.HasValue) {
                // suffix range: bytes=-N means last N bytes
                var suffixLen = (ulong)range.To.Value;
                startByte = session.FileSize > suffixLen ? session.FileSize - suffixLen : 0;
            }
        }

        if (startByte >= session.FileSize) {
            ctx.Response.StatusCode = 416;
            ctx.Response.Headers.ContentRange = $"bytes */{session.FileSize}";
            return;
        }

        var bytesToSend = requestedEndByte.HasValue ? requestedEndByte.Value - startByte : session.FileSize - startByte;

        ctx.Response.ContentType = session.ContentType;
        ctx.Response.ContentLength = (long)bytesToSend;

        if (isRangeRequest) {
            ctx.Response.StatusCode = 206;
            var endInclusive = requestedEndByte.HasValue ? requestedEndByte.Value - 1 : session.FileSize - 1;
            ctx.Response.Headers.ContentRange = $"bytes {startByte}-{endInclusive}/{session.FileSize}";
        }

        var filesClient = session.DaemonService.GetGrpcService<GRPCFilesService.GRPCFilesServiceClient>();

        var req = new SendFilesInput {
            Paths = { session.RemotePath },
            StartByte = startByte
        };
        if (requestedEndByte.HasValue)
            req.EndByte = requestedEndByte.Value;

        var sendFiles = filesClient.Internal_SendFiles(req, cancellationToken: ctx.RequestAborted);

        await foreach (var chunk in sendFiles.ResponseStream.ReadAllAsync(ctx.RequestAborted)) {
            if (chunk.DataCase == FileBuffer.DataOneofCase.Buffer)
                await ctx.Response.Body.WriteAsync(chunk.Buffer.Memory, ctx.RequestAborted);
        }
    }

    private static string GetContentType(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() switch {
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
