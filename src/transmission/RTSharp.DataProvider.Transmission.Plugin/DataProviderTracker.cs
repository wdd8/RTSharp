#nullable enable

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Abstractions.Client;

namespace RTSharp.DataProvider.Transmission.Plugin;

public class DataProviderTracker : IDataProviderTracker
{
    private Plugin ThisPlugin { get; }
    public IPluginHost PluginHost { get; }

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
        this.PluginHost = ThisPlugin.Host;
    }

    public async Task<IList<(Uri Uri, IList<Exception> Exceptions)>> Reannounce(byte[] TorrentHash, IList<Uri> TargetUris)
    {
        throw new NotImplementedException();
    }

    public async Task ReplaceTracker(Torrent Torrent, string Existing, string New, CancellationToken cancellationToken = default)
    {
        var client = PluginHost.AttachedDaemonService.GetTorrentsService(ThisPlugin.DataProvider.Instance);

        await client.ReplaceTracker(Torrent.Hash, Existing, New, cancellationToken);
    }
}
