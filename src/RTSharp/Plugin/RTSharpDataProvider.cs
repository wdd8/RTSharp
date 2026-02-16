#nullable enable

using Grpc.Core;

using Microsoft.Extensions.Configuration;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Abstractions.Daemon;
using RTSharp.Shared.Abstractions.DataProvider;
using RTSharp.Shared.Utils;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RTSharp.Plugin
{
    public class RTSharpDataProvider : IDataProviderHost
    {
        public RTSharpPlugin PluginInstance { get; }

        public IDataProvider Instance { get; }

        public IPlugin Plugin => PluginInstance.Instance;

        public DateTime TorrentChangesTaskStartedAt { get; set; } = DateTime.MinValue;

        public Task? CurrentTorrentChangesTask { get; set; }

        public CancellationTokenSource CurrentTorrentChangesTaskCts { get; set; }

        public DataProviderInstanceConfig? DataProviderInstanceConfig => PluginInstance.PluginConfig.GetSection("DataProvider").Get<DataProviderInstanceConfig?>();

        public Notifyable<DataProviderState> State { get; } = new();

        public RTSharpDataProvider(RTSharpPlugin PluginInstance, IDataProvider DataProvider)
        {
            this.PluginInstance = PluginInstance;
            this.Instance = DataProvider;
        }

        public override bool Equals(object? obj) => obj is RTSharpDataProvider dp && PluginInstance.InstanceId == dp.PluginInstance.InstanceId;

        public override int GetHashCode() => PluginInstance.InstanceId.GetHashCode();

        public static bool operator ==(RTSharpDataProvider? dp1, RTSharpDataProvider? dp2)
        {
            if (dp1 is null) {
                if (dp2 is null)
                    return true;

                return false;
            }
            if (dp2 is null) {
                if (dp1 is null)
                    return true;

                return false;
            }

            return dp1.Equals(dp2);
        }

        public static bool operator !=(RTSharpDataProvider? dp1, RTSharpDataProvider? dp2) => !(dp1 == dp2);

        public override string ToString() => $"{PluginInstance.PluginInstanceConfig.Name} ({PluginInstance.InstanceId})";

        private readonly HashSet<Guid> SupportedBuiltInDataProviders = new() {
            new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C"),
            new Guid("E347109B-A06D-4894-9E3A-6FFF63411370"),
            new Guid("0671CE03-09F4-4F07-9402-548CF2E201B1")
        };

        public Metadata GetBuiltInDataProviderGrpcHeaders()
        {
            if (!SupportedBuiltInDataProviders.Contains(PluginInstance.Instance.GUID)) {
                throw new InvalidOperationException("Not a built-in data provider");
            }

            return [
                new Metadata.Entry("data-provider", DataProviderInstanceConfig!.Name)
            ];
        }

        public IDaemonService AttachedDaemonService {
            get {
                var serverId = DataProviderInstanceConfig!.ServerId;
                if (serverId == null)
                    throw new InvalidOperationException("No singular server data providers registered");

                return Core.Servers.Value[serverId];
            }
        }
    }
}
