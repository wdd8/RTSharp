#nullable enable

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.DataProvider.Rtorrent.Plugin;

public class DataProviderTracker : IDataProviderTracker
{
    private Plugin ThisPlugin { get; }

    public DataProviderTrackerCapabilities Capabilities { get; } = new(
        AddNewTracker: false,
        EnableTracker: false,
        DisableTracker: false,
        RemoveTracker: false,
        ReannounceTracker: false,
        ReplaceTracker: true
    );

    public DataProviderTracker(Plugin ThisPlugin)
    {
        this.ThisPlugin = ThisPlugin;
    }

    public async Task<IList<(Uri Uri, IList<Exception> Exceptions)>> Reannounce(byte[] TorrentHash, IList<Uri> TargetUris)
    {
        throw new NotImplementedException();
    }

    public async Task ReplaceTracker(Torrent Torrent, string Existing, string New, CancellationToken cancellationToken = default)
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetTorrentsService(ThisPlugin.Host.Instance);

        await client.ReplaceTracker(Torrent.Hash, Existing, New, cancellationToken);
    }

    public async Task AddNewTracker(Torrent Torrent, string New, CancellationToken cancellationToken = default)
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetTorrentsService(ThisPlugin.Host.Instance);

        await client.AddNewTracker(Torrent.Hash, New, cancellationToken);
    }

    public async Task RemoveTracker(Torrent Torrent, string Existing, CancellationToken cancellationToken = default)
    {
        var client = ThisPlugin.Host.AttachedDaemonService!.GetTorrentsService(ThisPlugin.Host.Instance);

        await client.RemoveTracker(Torrent.Hash, Existing, cancellationToken);
    }
}
