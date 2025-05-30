using System.Text;
using RTSharp.Shared.Utils;
using RTSharp.Daemon.Protocols.DataProvider;
using Grpc.Core;
using Status = RTSharp.Daemon.Protocols.DataProvider.Status;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class TorrentOpService
    {
        private SCGICommunication Scgi;
        private SettingsService Settings;
    
        public TorrentOpService(IServiceProvider ServiceProvider, [ServiceKey] string InstanceKey)
        {
            this.Scgi = ServiceProvider.GetRequiredKeyedService<SCGICommunication>(InstanceKey);
            this.Settings = ServiceProvider.GetRequiredKeyedService<SettingsService>(InstanceKey);
        }
        
        public async Task<InfoHashDictionary<string>> GetDotTorrentFilePaths(IEnumerable<byte[]> Hashes)
        {
            var sessionSetting = (string)(await Settings.GetSettings(SettingsService.SessionPath))[SettingsService.SessionPath.RtorrentSetting];

            if (String.IsNullOrEmpty(sessionSetting))
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, "Received empty session path"));

            if (sessionSetting[^1] == '/')
                sessionSetting = sessionSetting[..^1];

            var ret = new InfoHashDictionary<string>();

            foreach (var hash in Hashes) {
                ret[hash] = $"{sessionSetting}/{Convert.ToHexString(hash)}.torrent";
            }

            return ret;
        }

        public async Task<TorrentsFilesReply> GetTorrentsFiles(IEnumerable<byte[]> Hashes)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in Hashes) {
                var sHash = Convert.ToHexString(hash);

                xml.Append("<value><struct><member><name>methodName</name><value><string>f.multicall</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value><value><string></string></value><value><string>f.size_bytes=</string></value><value><string>f.priority=</string></value><value><string>f.path=</string></value><value><string>f.completed_chunks=</string></value><value><string>f.prioritize_first=</string></value><value><string>f.prioritize_last=</string></value></data></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Scgi.Get(xml.ToString());

            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<params>\r\n<param><value><array><data>\r\n<value><array><data>\r\n<value><array><data>\r\n<value><array><data>\r\n<value><i8>2268332032</i8></value>\r\n<value><i8>1</i8></value>\r\n<value><string>ubuntusway-22.10-desktop-amd64.iso</string></value>\r\n<value><i8>4327</i8></value>\r\n<value><i8>0</i8></value>\r\n<value><i8>0</i8></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i8>262144</i8></value>\r\n<value><i8>1</i8></value>\r\n<value><string>.pad/262144</string></value>\r\n<value><i8>1</i8></value>\r\n<value><i8>0</i8></value>\r\n<value><i8>0</i8></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i8>113</i8></value>\r\n<value><i8>1</i8></value>\r\n<value><string>ubuntusway-22.10-desktop-amd64.md5.txt</string></value>\r\n<value><i8>2</i8></value>\r\n<value><i8>0</i8></value>\r\n<value><i8>0</i8></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i8>524175</i8></value>\r\n<value><i8>1</i8></value>\r\n<value><string>.pad/524175</string></value>\r\n<value><i8>1</i8></value>\r\n<value><i8>0</i8></value>\r\n<value><i8>0</i8></value>\r\n</data></array></value>\r\n<value><array><data>\r\n<value><i8>145</i8></value>\r\n<value><i8>1</i8></value>\r\n<value><string>ubuntusway-22.10-desktop-amd64.sha256.txt</string></value>\r\n<value><i8>2</i8></value>\r\n<value><i8>0</i8></value>\r\n<value><i8>0</i8></value>\r\n</data></array></value>\r\n</data></array></value>\r\n</data></array></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<params>\r\n<param><value><array><data>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            XMLUtils.FaultStatus? fault;
            if ((fault = XMLUtils.TryGetFaultStruct(ref result, "f.multicall")) != null)
                throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, fault.FaultCode + ": " + fault.FaultString));

            var ret = new TorrentsFilesReply();
            foreach (var hash in Hashes) {
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                var torrent = new TorrentsFilesReply.Types.TorrentsFiles() {
                    InfoHash = hash.ToByteString()
                };

                while (!XMLUtils.CheckFor(result, XMLUtils.DATA_TOKEN_END)) {
                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);

                    var size = XMLUtils.GetValue<ulong>(ref result);
                    var priority = XMLUtils.GetValue<ulong>(ref result);
                    var path = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                    var completedChunks = XMLUtils.GetValue<ulong>(ref result);
                    var prioritizeFirst = XMLUtils.GetValue<bool>(ref result);
                    var prioritizeLast = XMLUtils.GetValue<bool>(ref result);

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                    TorrentsFilesReply.Types.FileDownloadStrategy downloadStrategy = 0;
                    if (!prioritizeFirst && !prioritizeLast)
                        downloadStrategy = TorrentsFilesReply.Types.FileDownloadStrategy.Normal;
                    if (prioritizeFirst)
                        downloadStrategy |= TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeFirst;
                    if (prioritizeFirst)
                        downloadStrategy |= TorrentsFilesReply.Types.FileDownloadStrategy.PrioritizeLast;

                    torrent.Files.Add(new TorrentsFilesReply.Types.File() {
                        Size = size,
                        Priority = priority switch {
                            0 => TorrentsFilesReply.Types.FilePriority.DontDownload,
                            1 => TorrentsFilesReply.Types.FilePriority.Normal,
                            2 => TorrentsFilesReply.Types.FilePriority.High,
                            _ => TorrentsFilesReply.Types.FilePriority.Normal // TODO?
                        },
                        Path = path,
                        DownloadedPieces = completedChunks,
                        DownloadStrategy = downloadStrategy
                    });
                }

                ret.Reply.Add(torrent);

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
            }

            return ret;
        }

        public async Task<InfoHashDictionary<(string BasePath, string? ContainerFolder)>> GetTorrentsBasePath(IEnumerable<byte[]> Hashes)
        {
            var xml = new StringBuilder("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var hash in Hashes) {
                var hexStr = Convert.ToHexString(hash);
                xml.Append("<value><struct><member><name>methodName</name><value><string>d.directory</string></value></member><member><name>params</name><value><array><data><value><string>" + hexStr + "</string></value></data></array></value></member></struct></value>");
                xml.Append("<value><struct><member><name>methodName</name><value><string>d.is_multi_file</string></value></member><member><name>params</name><value><array><data><value><string>" + hexStr + "</string></value></data></array></value></member></struct></value>");
            }
            xml.Append("</data></array></value></param></params></methodCall>");
            var result = await Scgi.Get(xml.ToString());

            // <?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<params>\r\n<param><value><array><data>\r\n<value><array><data>\r\n<value><string>/home/user/torrentdata/smartos-20221201T010802Z</string></value>\r\n</data></array></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            // <value><array><data>\r\n<value><string>/home/user/torrentdata/smartos-20221201T010802Z</string></value>\r\n</data></array></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n

            var basePaths = new InfoHashDictionary<(string BasePath, string ContainerFolder)>();

            foreach (var hash in Hashes) {
                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    var status = XMLUtils.GetFaultStruct(ref result, "d.directory");
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, $"Failed to get name for {Convert.ToHexString(hash)}. " + status.ToFailureString()));
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                var directory = XMLUtils.Decode(XMLUtils.GetValue<string>(ref result));
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                    var status = XMLUtils.GetFaultStruct(ref result, "d.is_multi_file");
                    throw new RpcException(new global::Grpc.Core.Status(StatusCode.Internal, $"Failed to get is_multi_file for {Convert.ToHexString(hash)}. " + status.ToFailureString()));
                }

                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                var isMultiFile = XMLUtils.GetValue<bool>(ref result);
                XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);

                if (isMultiFile) {
                    // If multifile remove d.name from path. Base path shouldn't point to any inner files of torrent
                    var dirs = directory.Split(Path.DirectorySeparatorChar);

                    // Can d.name be used instead if we do d.directory_base.set (rename)?
                    directory = String.Join(Path.DirectorySeparatorChar, dirs[..^1]);

                    basePaths[hash] = (directory, dirs[^1]);
                } else {
                    basePaths[hash] = (directory, null);
                }
            }

            return basePaths;
        }
    }
}
