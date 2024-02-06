using Grpc.Core;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RTSharp.Shared.Utils;

namespace RTSharp.DataProvider.Rtorrent.Server.Services
{
    public class TorrentsService
    {
        private readonly SCGICommunication Scgi;
        private readonly SettingsService Settings;

        public TorrentsService(SCGICommunication Scgi, SettingsService Settings)
        {
            this.Scgi = Scgi;
            this.Settings = Settings;
        }

        public async Task<InfoHashDictionary<string>> GetDotTorrentFilePaths(IEnumerable<byte[]> Hashes)
        {
            var sessionSetting = (string)(await Settings.GetSettings(Services.SettingsService.SessionPath))[Services.SettingsService.SessionPath.RtorrentSetting];

            if (String.IsNullOrEmpty(sessionSetting))
                throw new RpcException(new Grpc.Core.Status(StatusCode.Internal, "Received empty session path"));

            if (sessionSetting[^1] == '/')
                sessionSetting = sessionSetting[..^1];

            var ret = new InfoHashDictionary<string>();

            foreach (var hash in Hashes) {
                ret[hash] = $"{sessionSetting}/{Convert.ToHexString(hash)}.torrent";
            }

            return ret;
        }
    }
}
