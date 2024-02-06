#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
    public class DataProviderTracker : IDataProviderTracker
    {
        private Plugin ThisPlugin { get; }
        public IPluginHost PluginHost { get; }

        public DataProviderTrackerCapabilities Capabilities { get; } = new(
            AddNewTracker: false,
            EnableTracker: false,
            DisableTracker: false,
            RemoveTracker: false,
            ReannounceTracker: false
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
    }
}
