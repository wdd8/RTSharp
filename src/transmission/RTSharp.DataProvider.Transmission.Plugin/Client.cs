global using static RTSharp.DataProvider.Transmission.Plugin.GlobalClient;

using Microsoft.Extensions.Configuration;

using RTSharp.Shared.Abstractions;

using Transmission.Net;
using Transmission.Net.Core;

namespace RTSharp.DataProvider.Transmission.Plugin
{
	public static class GlobalClient
	{
		public static ITransmissionClient Client;

		public static IPluginHost PluginHost { get; set; }

		public static void Init()
		{
			if (Client == null) {
				try {
					var uri = PluginHost.PluginConfig.GetValue<string>("Server:Uri")!;
					var username = PluginHost.PluginConfig.GetValue<string>("Server:Username");
					var password = PluginHost.PluginConfig.GetValue<string>("Server:Password");

                    Client = new TransmissionClient(uri, null, username, password);
				} catch {
					Client = null;
				}
			}
		}
	}
}
