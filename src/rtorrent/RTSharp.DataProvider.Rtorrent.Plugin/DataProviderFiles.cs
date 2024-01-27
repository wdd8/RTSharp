#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using RTSharp.DataProvider.Rtorrent.Plugin.Server;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

namespace RTSharp.DataProvider.Rtorrent.Plugin
{
    public class DataProviderFiles : IDataProviderFiles
    {
        private Plugin ThisPlugin { get; }
		public IPluginHost PluginHost { get; }

		public DataProviderFilesCapabilities Capabilities { get; } = new(
			GetDotTorrents: true,
			GetDefaultSavePath: true
		);

		public DataProviderFiles(Plugin ThisPlugin)
	    {
            this.ThisPlugin = ThisPlugin;
		    this.PluginHost = ThisPlugin.Host;
		}

		public async Task<InfoHashDictionary<byte[]>> GetDotTorrents(IList<byte[]> In)
		{
			var client = Clients.Torrent();

			var res = client.GetDotTorrents(new Protocols.Torrents() { Hashes = { In.Select(x => Common.Extensions.ToByteString(x)) } });

			var meta = new Dictionary<string, byte[]>();
			var dotTorrents = new Dictionary<string, MemoryStream>();

			await foreach (var data in res.ResponseStream.ReadAllAsync()) {
				switch (data.DataCase) {
					case Protocols.DotTorrentsData.DataOneofCase.TMetadata:
						if (meta.ContainsKey(data.TMetadata.Id)) {
							throw new InvalidOperationException("Received duplicate metadata on hash");
						}

						dotTorrents[data.TMetadata.Id] = new MemoryStream();
						meta[data.TMetadata.Id] = data.TMetadata.Hash.ToByteArray();
						break;
					case Protocols.DotTorrentsData.DataOneofCase.TData:
						dotTorrents[data.TData.Id].Write(data.TData.Chunk.Span);
						break;
				}
			}

			var ret = new InfoHashDictionary<byte[]>();
			foreach (var (k, v) in dotTorrents) {
				ret[meta[k]] = v.ToArray();
				v.Dispose();
			}

			return ret;
		}

		public async Task<string> GetDefaultSavePath()
		{
			var client = Clients.Settings();
			var settings = await client.GetSettingsAsync(new Empty());

			PluginHost.Logger.Verbose($"Default save path: {settings.DefaultDirectoryForDownloads}");

			return settings.DefaultDirectoryForDownloads;
		}
	}
}
