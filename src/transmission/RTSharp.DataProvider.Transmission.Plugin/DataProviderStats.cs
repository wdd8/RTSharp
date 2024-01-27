using RTSharp.Shared.Abstractions;

namespace RTSharp.DataProvider.Transmission.Plugin
{
	public class DataProviderStats : IDataProviderStats
	{
		private Plugin ThisPlugin { get; }
		public IPluginHost PluginHost { get; }

		public DataProviderStatsCapabilities Capabilities { get; } = new(
			GetStateHistory: false
		);

		public DataProviderStats(Plugin ThisPlugin, Action Init)
		{
			this.ThisPlugin = ThisPlugin;
			this.PluginHost = ThisPlugin.Host;
		}
	}
}
