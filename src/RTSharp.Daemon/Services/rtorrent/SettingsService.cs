using System.Diagnostics;
using System.Text;
using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Protocols.DataProvider.Settings;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class SettingsService
    {
        private readonly SCGICommunication Scgi;
        private readonly RtorrentVersionService VersionService;
        private readonly ILogger<SettingsService> Logger;

        public SettingsService(IServiceProvider ServiceProvider, [ServiceKey] string InstanceKey, ILogger<SettingsService> Logger)
        {
            this.Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            this.VersionService = ServiceProvider.GetRequiredKeyedService<RtorrentVersionService>(InstanceKey);
            this.Logger = Logger;
        }

        public record SettingSpec(string RtorrentSetting, SCGI_DATA_TYPE DataType, string Property);

        private static readonly SettingSpec[] CommonSettings = new[] {
            new SettingSpec("throttle.max_uploads", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.NumberOfUploadSlots)),
            new SettingSpec("throttle.min_peers.normal", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MinimumNumberOfPeers)),
            new SettingSpec("throttle.max_peers.normal", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumNumberOfPeers)),
            new SettingSpec("throttle.min_peers.seed", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MinimumNumberOfPeersForSeeding)),
            new SettingSpec("throttle.max_peers.seed", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumNumberOfPeersForSeeding)),
            new SettingSpec("trackers.numwant", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.WishedNumberOfPeers)),
            new SettingSpec("pieces.hash.on_completion", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.CheckHashAfterDownload)),
            new SettingSpec("directory.default", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.DefaultDirectoryForDownloads)),

            new SettingSpec("throttle.global_up.max_rate", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumUploadRate)),
            new SettingSpec("throttle.global_down.max_rate", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumDownloadRate)),

            new SettingSpec("throttle.max_uploads.global", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.GlobalNumberOfUploadSlots)),
            new SettingSpec("throttle.max_downloads.global", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.GlobalNumberOfDownloadSlots)),
            new SettingSpec("pieces.memory.max", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumMemoryUsage)),

            new SettingSpec("protocol.pex", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.EnablePeerExchange)),
            new SettingSpec("network.local_address", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.IpToReportToTracker)),

            new SettingSpec("network.http.cacert", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkHttpCacert)),
            new SettingSpec("network.http.capath", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkHttpCapath)),

            new SettingSpec("throttle.max_downloads.div", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.ThrottleMaxDownloadsDiv)),
            new SettingSpec("throttle.max_uploads.div", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.ThrottleMaxUploadsDiv)),
            new SettingSpec("system.file.max_size", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemFileMaxSize)),

            new SettingSpec("pieces.preload.type", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.PiecesPreloadType)), // Special handling
            new SettingSpec("pieces.preload.min_size", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.PiecesPreloadMinSize)),
            new SettingSpec("pieces.preload.min_rate", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.PiecesPreloadMinRate)),

            new SettingSpec("network.receive_buffer.size", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.NetworkReceiveBufferSize)),
            new SettingSpec("network.send_buffer.size", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.NetworkSendBufferSize)),

            new SettingSpec("pieces.sync.always_safe", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.PiecesSyncAlwaysSafe)),
            new SettingSpec("pieces.sync.timeout_safe", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.PiecesSyncTimeoutSafe)),
            new SettingSpec("pieces.sync.timeout", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.PiecesSyncTimeout)),

            new SettingSpec("network.scgi.dont_route", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.NetworkScgiDontRoute)),

            new SettingSpec("session.path", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.SessionPath)),
            new SettingSpec("session.use_lock", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.SessionUseLock)),
            new SettingSpec("session.on_completion", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.SessionOnCompletion)),

            new SettingSpec("system.file.split_size", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemFileSplitSize)),
            new SettingSpec("system.file.split_suffix", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.SystemFileSplitSuffix)),

            new SettingSpec("network.bind_address", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkBindAddress))
        };

        public static SettingSpec[] GetSettingSpecs(bool IsV16)
        {
            var ret = new List<SettingSpec>(CommonSettings);

            if (IsV16) {
                ret.Add(new SettingSpec("network.listen.port.range", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.PortUsedForIncomingConnections)));
                ret.Add(new SettingSpec("network.listen.port.random", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.RandomizePort)));

                ret.Add(new SettingSpec("system.sockets.files.min_alloc", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemSocketsFilesMinAlloc)));
                ret.Add(new SettingSpec("system.sockets.files.max_alloc", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemSocketsFilesMaxAlloc)));
                ret.Add(new SettingSpec("system.sockets.http.min_alloc", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemSocketsHttpMinAlloc)));
                ret.Add(new SettingSpec("system.sockets.http.max_alloc", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.SystemSocketsHttpMaxAlloc)));

                ret.Add(new SettingSpec("dht.override_port", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.DhtPort)));

                ret.Add(new SettingSpec("network.proxy.http", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkHttpProxyAddress)));
                ret.Add(new SettingSpec("network.proxy.global", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkProxyAddress)));
            } else {
                ret.Add(new SettingSpec("network.port_open", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.OpenListeningPort)));
                ret.Add(new SettingSpec("network.port_range", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.PortUsedForIncomingConnections)));
                ret.Add(new SettingSpec("network.port_random", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.RandomizePort)));

                ret.Add(new SettingSpec("network.max_open_files", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumNumberOfOpenFiles)));
                ret.Add(new SettingSpec("network.http.max_open", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.MaximumNumberOfOpenHttpConnections)));

                ret.Add(new SettingSpec("dht.port", SCGI_DATA_TYPE.I8, nameof(RtorrentSettings.DhtPort)));

                ret.Add(new SettingSpec("trackers.use_udp", SCGI_DATA_TYPE.BOOL_AS_I8, nameof(RtorrentSettings.TrackersUseUdp)));
                ret.Add(new SettingSpec("network.http.proxy_address", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkHttpProxyAddress)));
                ret.Add(new SettingSpec("network.proxy_address", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.NetworkProxyAddress)));
            }

            return [.. ret];
        }

        public static string[] GetUnsupportedProperties(bool IsV16)
        {
            var supported = GetSettingSpecs(IsV16).Select(x => x.Property).ToHashSet(StringComparer.Ordinal);

            return [.. GetSettingSpecs(true)
                .Concat(GetSettingSpecs(false))
                .Select(x => x.Property)
                .Distinct(StringComparer.Ordinal)
                .Where(x => !supported.Contains(x))];
        }

        public async Task<SettingSpec[]> GetConnectedSettingSpecs(CancellationToken CancellationToken = default) =>
            GetSettingSpecs(await VersionService.IsV16(CancellationToken));

        public static readonly SettingSpec SessionPath = new SettingSpec("session.path", SCGI_DATA_TYPE.STRING, nameof(RtorrentSettings.SessionPath));

        public async Task<Dictionary<string, object>> GetSettings(params SettingSpec[] Settings)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var setting in Settings) {
                xml.Append("<value><struct><member><name>methodName</name><value><string>" + setting.RtorrentSetting + "</string></value></member><member><name>params</name><value><array><data /></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");
            using var response = await Scgi.Get(xml.ToString());
            var result = response.Reader();
            var ret = new Dictionary<string, object>();

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            foreach (var setting in Settings) {
                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    var fault = XMLUtils.GetFaultStruct(ref result, setting.RtorrentSetting);
                    Logger.LogWarning($"{setting.RtorrentSetting} was rejected. Leaving {setting.Property} unset: {fault.ToFailureString()}");
                    continue;
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                var expectedType = setting.DataType == SCGI_DATA_TYPE.BOOL_AS_I8 ? SCGI_DATA_TYPE.I8 : setting.DataType;
                var actualType = XMLUtils.GetValueType(result);

                if (actualType != expectedType)
                    throw new InvalidDataException($"{setting.RtorrentSetting} returned {actualType}, expected {expectedType}:{Environment.NewLine}{XMLUtils.DescribePosition(result)}");

                if (setting.RtorrentSetting == "pieces.preload.type") {
                    var val = XMLUtils.GetValue<long>(ref result);

                    ret[setting.Property] = val switch {
                        0 => "off",
                        1 => "madvise",
                        2 => "direct paging",
                        _ => "unknown"
                    };
                } else {
                    switch (setting.DataType) {
                        case SCGI_DATA_TYPE.I8:
                            ret[setting.Property] = XMLUtils.GetValue<long>(ref result);
                            break;
                        case SCGI_DATA_TYPE.BOOL_AS_I8:
                            ret[setting.Property] = XMLUtils.GetValue<bool>(ref result);
                            break;
                        case SCGI_DATA_TYPE.STRING:
                            ret[setting.Property] = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }

        public async Task<CommandReply> SetSettings(RtorrentSettings Req, CancellationToken CancellationToken = default)
        {
            var allSettings = await GetConnectedSettingSpecs(CancellationToken);
            var currentSettings = await GetSettings(allSettings);

            var piecesPreloadType = Req.PiecesPreloadType switch {
                "off" => 0,
                "madvise" => 1,
                "direct paging" => 2,
                _ => throw new ArgumentOutOfRangeException(nameof(Req.PiecesPreloadType))
            };

            var sentSettings = new List<SettingSpec>();

            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var setting in allSettings) {
                if (!currentSettings.TryGetValue(setting.Property, out var currentValue))
                    continue;

                var prop = Req.GetType().GetProperty(setting.Property) ?? throw new InvalidOperationException();
                var newValue = prop.GetValue(Req);

                if (currentValue.ToString() == newValue?.ToString())
                    continue;

                sentSettings.Add(setting);

                xml.Append("<value><struct><member><name>methodName</name><value><string>" + setting.RtorrentSetting + ".set</string></value></member><member><name>params</name><value><array><data><value><string></string></value><value>");

                string valueToSend;

                if (setting.RtorrentSetting == "pieces.preload.type")
                    valueToSend = piecesPreloadType.ToString();
                else
                    valueToSend = newValue?.ToString() ?? "";

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

            using var response = await Scgi.Get(xml.ToString());
            var result = response.Reader();

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);
            var ret = new CommandReply();

            foreach (var setting in sentSettings) {
                Debug.Assert(result.Remaining > 15);

                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    //<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-503</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not set local address: Name or service not known.</string></value></member>\r\n</struct></value>\r\n
                    ret.Response.Add(GrpcExtensions.ToStatus(XMLUtils.GetFaultStruct(ref result, setting.RtorrentSetting)));
                    continue;
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                Debug.Assert(XMLUtils.CheckFor(result, "<v"u8));

                var responseType = XMLUtils.GetValueType(result);

                object val = responseType switch {
                    SCGI_DATA_TYPE.I4 => XMLUtils.GetValue<int>(ref result),
                    SCGI_DATA_TYPE.I8 => XMLUtils.GetValue<long>(ref result),
                    SCGI_DATA_TYPE.BOOL_AS_I8 => XMLUtils.GetValue<bool>(ref result),
                    SCGI_DATA_TYPE.STRING => XMLUtils.Decode(XMLUtils.GetValue<string>(ref result)),
                    _ => throw new ArgumentOutOfRangeException()
                };

                ret.Response.Add(GrpcExtensions.SuccessfulStatus(setting.RtorrentSetting));

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }
    }
}
