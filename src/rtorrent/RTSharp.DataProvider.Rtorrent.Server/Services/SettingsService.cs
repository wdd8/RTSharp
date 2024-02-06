using RTSharp.DataProvider.Rtorrent.Server.Utils;
using System.Diagnostics;
using System.Text;
using System;
using System.Threading.Tasks;
using RTSharp.DataProvider.Rtorrent.Protocols.Types;
using System.Collections.Generic;
using RTSharp.DataProvider.Rtorrent.Common;

namespace RTSharp.DataProvider.Rtorrent.Server.Services
{
    public class SettingsService
    {
        private readonly SCGICommunication Scgi;

        public SettingsService(SCGICommunication Scgi)
        {
            this.Scgi = Scgi;
        }

        public record SettingSpec(string RtorrentSetting, SCGI_DATA_TYPE DataType, string Property);

        public static readonly SettingSpec[] AllSettings = new[] {
            new SettingSpec("throttle.max_uploads", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.ThrottleMaxUploadsDiv)),
            new SettingSpec("throttle.min_peers.normal", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MinimumNumberOfPeers)),
            new SettingSpec("throttle.max_peers.normal", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumNumberOfPeers)),
            new SettingSpec("throttle.min_peers.seed", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MinimumNumberOfPeersForSeeding)),
            new SettingSpec("throttle.max_peers.seed", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumNumberOfPeersForSeeding)),
            new SettingSpec("trackers.numwant", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.WishedNumberOfPeers)),
            new SettingSpec("pieces.hash.on_completion", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.CheckHashAfterDownload)),
            new SettingSpec("directory.default", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.DefaultDirectoryForDownloads)),

            new SettingSpec("network.port_open", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.OpenListeningPort)),
            new SettingSpec("network.port_range", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.PortUsedForIncomingConnections)),
            new SettingSpec("network.port_random", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.RandomizePort)),

            new SettingSpec("throttle.global_up.max_rate", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumUploadRate)),
            new SettingSpec("throttle.global_down.max_rate", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumDownloadRate)),

            new SettingSpec("throttle.max_uploads.global", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.GlobalNumberOfDownloadSlots)),
            new SettingSpec("throttle.max_downloads.global", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.GlobalNumberOfUploadSlots)),
            new SettingSpec("pieces.memory.max", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumMemoryUsage)),
            new SettingSpec("network.max_open_files", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumNumberOfOpenFiles)),
            new SettingSpec("network.http.max_open", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.MaximumNumberOfOpenHttpConnections)),

            new SettingSpec("dht.port", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.DhtPort)),
            new SettingSpec("protocol.pex", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.EnablePeerExchange)),
            new SettingSpec("network.local_address", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.IpToReportToTracker)),

            new SettingSpec("network.http.cacert", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.NetworkHttpCacert)),
            new SettingSpec("network.http.capath", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.NetworkHttpCapath)),

            new SettingSpec("throttle.max_downloads.div", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.ThrottleMaxDownloadsDiv)),
            new SettingSpec("throttle.max_uploads.div", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.ThrottleMaxUploadsDiv)),
            new SettingSpec("system.file.max_size", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.SystemFileMaxSize)),

            new SettingSpec("pieces.preload.type", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.PiecesPreloadType)), // Special handling
            new SettingSpec("pieces.preload.min_size", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.PiecesPreloadMinSize)),
            new SettingSpec("pieces.preload.min_rate", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.PiecesPreloadMinRate)),

            new SettingSpec("network.receive_buffer.size", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.NetworkReceiveBufferSize)),
            new SettingSpec("network.send_buffer.size", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.NetworkSendBufferSize)),

            new SettingSpec("pieces.sync.always_safe", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.PiecesSyncAlwaysSafe)),
            new SettingSpec("pieces.sync.timeout_safe", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.PiecesSyncTimeoutSafe)),
            new SettingSpec("pieces.sync.timeout", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.PiecesSyncTimeout)),

            new SettingSpec("network.scgi.dont_route", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.NetworkScgiDontRoute)),

            new SettingSpec("session.path", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.SessionPath)),
            new SettingSpec("session.use_lock", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.SessionUseLock)),
            new SettingSpec("session.on_completion", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.SessionOnCompletion)),

            new SettingSpec("system.file.split_size", SCGI_DATA_TYPE.I8, nameof(Protocols.Settings.SystemFileSplitSize)),
            new SettingSpec("system.file.split_suffix", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.SystemFileSplitSuffix)),

            new SettingSpec("trackers.use_udp", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(Protocols.Settings.TrackersUseUdp)),

            new SettingSpec("network.http.proxy_address", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.NetworkHttpProxyAddress)),
            new SettingSpec("network.proxy_address", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.NetworkProxyAddress)),
            new SettingSpec("network.bind_address", SCGI_DATA_TYPE.STRING, nameof(Protocols.Settings.NetworkBindAddress))
        };

        public static readonly SettingSpec SessionPath = new SettingSpec("session.path", SCGI_DATA_TYPE.STRING, null);

        public async Task<Dictionary<string, object>> GetSettings(params SettingSpec[] Settings)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var setting in Settings) {
                xml.Append("<value><struct><member><name>methodName</name><value><string>" + setting.RtorrentSetting + "</string></value></member><member><name>params</name><value><array><data /></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");
            var result = await Scgi.Get(xml.ToString());
            var ret = new Dictionary<string, object>();

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            foreach (var setting in Settings) {
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                Debug.Assert(result[..2].Span.SequenceEqual(stackalloc[] { (byte)'<', (byte)'v' }));

#if DEBUG
                switch (setting.DataType) {
                    case SCGI_DATA_TYPE.I8:
                    case SCGI_DATA_TYPE.BOOL_AS_I8:
                        Debug.Assert(result[..11].Span.SequenceEqual(Encoding.UTF8.GetBytes("<value><i8>")));
                        break;
                    case SCGI_DATA_TYPE.STRING:
                        Debug.Assert(result[..15].Span.SequenceEqual(Encoding.UTF8.GetBytes("<value><string>")));
                        break;
                }
#endif

                if (setting.RtorrentSetting == "pieces.preload.type") {
                    var val = XMLUtils.GetValue<long>(ref result);

                    ret[setting.RtorrentSetting] = val switch {
                        0 => "off",
                        1 => "madvise",
                        2 => "direct paging",
                        _ => "unknown"
                    };
                } else {
                    switch (setting.DataType) {
                        case SCGI_DATA_TYPE.I8:
                            ret[setting.RtorrentSetting] = XMLUtils.GetValue<long>(ref result);
                            break;
                        case SCGI_DATA_TYPE.BOOL_AS_I8:
                            ret[setting.RtorrentSetting] = XMLUtils.GetValue<bool>(ref result);
                            break;
                        case SCGI_DATA_TYPE.STRING:
                            ret[setting.RtorrentSetting] = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                Debug.Assert(result[..2].Span.SequenceEqual(stackalloc[] { (byte)'<', (byte)'/' }));
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }

        public async Task<CommandReply> SetSettings(Protocols.Settings Req)
        {
            var currentSettings = await GetSettings(AllSettings);

            var piecesPreloadType = Req.PiecesPreloadType switch {
                "off" => 0,
                "madvise" => 1,
                "direct paging" => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(Req.PiecesPreloadType))
            };

            var sentSettings = new List<Services.SettingsService.SettingSpec>();

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var setting in AllSettings) {
                var currentValue = currentSettings[setting.RtorrentSetting];
                var newValue = Req.GetType().GetProperty(setting.Property).GetValue(Req);

                if (currentValue.ToString() == newValue.ToString())
                    continue;

                sentSettings.Add(setting);

                xml.Append("<value><struct><member><name>methodName</name><value><string>" + setting.RtorrentSetting + ".set</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value>");

                string valueToSend;

                if (setting.RtorrentSetting == "pieces.preload.type")
                    valueToSend = piecesPreloadType.ToString();
                else
                    valueToSend = newValue.ToString();

                switch (setting.DataType) {
                    case SCGI_DATA_TYPE.I8:
                        xml.Append("<i8>" + valueToSend + "</i8>");
                        break;
                    case SCGI_DATA_TYPE.BOOL_AS_I8:
                        xml.Append("<i8>" + (valueToSend == "True" ? "1" : "0") + "</i8>");
                        break;
                    case SCGI_DATA_TYPE.STRING:
                        xml.Append("<string>" + valueToSend + "</string>");
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                xml.Append("</value></data></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Scgi.Get(xml.ToString());

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);
            var ret = new CommandReply();

            foreach (var setting in sentSettings) {
                Debug.Assert(result.Length > 15);

                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    //<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-503</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not set local address: Name or service not known.</string></value></member>\r\n</struct></value>\r\n
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);
                    ret.Response.Add(XMLUtils.GetFaultStruct(ref result, setting.RtorrentSetting));
                    continue;
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                Debug.Assert(result[..2].Span.SequenceEqual(stackalloc[] { (byte)'<', (byte)'v' }));

                var responseType = XMLUtils.GetValueType(result);

                object val = responseType switch {
                    SCGI_DATA_TYPE.I4 => XMLUtils.GetValue<int>(ref result),
                    SCGI_DATA_TYPE.I8 => XMLUtils.GetValue<long>(ref result),
                    SCGI_DATA_TYPE.BOOL_AS_I8 => XMLUtils.GetValue<bool>(ref result),
                    SCGI_DATA_TYPE.STRING => XMLUtils.Decode(XMLUtils.GetValue<string>(ref result)),
                    _ => throw new ArgumentOutOfRangeException()
                };

                ret.Response.Add(Extensions.SuccessfulStatus(setting.RtorrentSetting));

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }
    }
}
