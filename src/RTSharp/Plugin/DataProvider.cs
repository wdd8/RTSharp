#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Grpc.Core;

using Microsoft.Extensions.Configuration;

using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.Plugin
{
    public class DataProvider : IHostedDataProvider
    {
        public PluginInstance PluginInstance { get; }

        public IDataProvider Instance { get; }

        public DateTime TorrentChangesTaskStartedAt { get; set; } = DateTime.MinValue;

        public Task? CurrentTorrentChangesTask { get; set; }

        public CancellationTokenSource CurrentTorrentChangesTaskCts { get; set; }

        public DataProviderInstanceConfig? DataProviderInstanceConfig => PluginInstance.PluginConfig.GetSection("DataProvider").Get<DataProviderInstanceConfig?>();

        public Notifyable<DataProviderState> State { get; } = new();

        public DataProvider(PluginInstance PluginInstance, IDataProvider DataProvider)
        {
            this.PluginInstance = PluginInstance;
            this.Instance = DataProvider;
        }

        public override bool Equals(object? obj) => obj is DataProvider dp && PluginInstance.InstanceId == dp.PluginInstance.InstanceId;

        public override int GetHashCode() => PluginInstance.InstanceId.GetHashCode();

        public static bool operator ==(DataProvider? dp1, DataProvider? dp2)
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

        public static bool operator !=(DataProvider? dp1, DataProvider? dp2) => !(dp1 == dp2);

        public override string ToString() => $"{PluginInstance.PluginInstanceConfig.Name} ({PluginInstance.InstanceId})";

        private readonly Dictionary<Guid, string> SupportedBuiltInDataProviders = new() {
            { new Guid("90F180F2-F1D3-4CAA-859F-06D80B5DCF5C"), "rtorrent" }
        };

        public Metadata GetBuiltInDataProviderGrpcHeaders()
        {
            if (!SupportedBuiltInDataProviders.TryGetValue(PluginInstance.Instance.GUID, out var name)) {
                throw new InvalidOperationException("Not a built-in data provider");
            }

            return [
                new Metadata.Entry("data-provider", name)
            ];
        }
    }
}
