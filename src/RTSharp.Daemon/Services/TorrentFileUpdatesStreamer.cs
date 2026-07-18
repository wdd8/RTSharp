using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.Services;

public static class TorrentFileUpdatesStreamer
{
    public static async Task Stream(
        Func<CancellationToken, Task<TorrentsFilesReply.Types.TorrentsFiles>> Fetch,
        TimeSpan Interval,
        IServerStreamWriter<DeltaTorrentsFilesResponse> Res,
        CancellationToken CancellationToken)
    {
        if (Interval < TimeSpan.FromMilliseconds(10))
            throw new RpcException(new global::Grpc.Core.Status(StatusCode.InvalidArgument, "Interval cannot be lower than 10ms"));

        TorrentsFilesReply.Types.TorrentsFiles? snapshot = null;

        while (!CancellationToken.IsCancellationRequested) {
            var current = await Fetch(CancellationToken);

            if (DiffStaticProperties(snapshot, current)) {
                await Res.WriteAsync(new DeltaTorrentsFilesResponse {
                    FullUpdate = current
                }, CancellationToken);
            } else {
                DeltaTorrentsFilesResponse? resp = null;

                for (var x = 0; x < current.Files.Count; x++) {
                    var prev = snapshot!.Files[x];
                    var cur = current.Files[x];

                    if (prev.Downloaded != cur.Downloaded ||
                        prev.DownloadedPieces != cur.DownloadedPieces ||
                        prev.Priority != cur.Priority ||
                        prev.DownloadStrategy != cur.DownloadStrategy) {
                        resp ??= new DeltaTorrentsFilesResponse();
                        resp.Changed.Add(new DeltaFileResponse {
                            Index = (uint)x,
                            DownloadedPieces = cur.DownloadedPieces,
                            Downloaded = cur.Downloaded,
                            Priority = cur.Priority,
                            DownloadStrategy = cur.DownloadStrategy
                        });
                    }
                }

                if (resp != null)
                    await Res.WriteAsync(resp, CancellationToken);
            }

            snapshot = current;

            try {
                await Task.Delay(Interval, CancellationToken);
            } catch (OperationCanceledException) {
                return;
            }
        }
    }

    private static bool DiffStaticProperties(TorrentsFilesReply.Types.TorrentsFiles? Snapshot, TorrentsFilesReply.Types.TorrentsFiles Current)
    {
        if (Snapshot == null)
            return true;
        if (Snapshot.MultiFile != Current.MultiFile)
            return true;
        if (Snapshot.Files.Count != Current.Files.Count)
            return true;

        for (var i = 0; i < Current.Files.Count; i++) {
            if (Snapshot.Files[i].Path != Current.Files[i].Path || Snapshot.Files[i].Size != Current.Files[i].Size)
                return true;
        }

        return false;
    }
}
