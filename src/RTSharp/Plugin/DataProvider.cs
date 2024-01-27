using System;
using System.Threading.Tasks;

using RTSharp.Shared.Abstractions;

namespace RTSharp.Plugin
{
    public class DataProvider
    {
        public PluginInstance PluginInstance { get; }

        public IDataProvider Instance { get; }

        public DateTime TaskStartedAt { get; set; } = DateTime.MinValue;

        public Task? CurrentTask { get; set; }

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
	}
}
