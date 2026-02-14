using System.Diagnostics;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class SCGICommunication
    {
        private ConfigModel Config { get; }
        public ILogger<SCGICommunication> Logger { get; }

        public SCGICommunication(IOptionsFactory<ConfigModel> Opts, [ServiceKey] string InstanceKey, ILogger<SCGICommunication> Logger)
        {
            this.Config = Opts.Create(InstanceKey);
            this.Logger = Logger;
        }

#if DEBUG
        private static string WslAddress;
#endif

        public async Task<ReadOnlyMemory<byte>> Get(string Xml, CancellationToken CancellationToken = default)
        {
            var payload = SCGIPayloadBuilder.BuildPayload(Xml);
            NetworkStream stm;
            var listenPath = Config.SCGIListen;
            Action disconnect;

#if DEBUG
            if (listenPath == "wsl") {
                if (WslAddress == null) {
                    var si = new ProcessStartInfo() {
                        FileName = "wsl",
                        ArgumentList = { "--", "ip", "-o", "-4", "addr", "list", "eth0" },
                        RedirectStandardOutput = true
                    };

                    var proc = Process.Start(si);
                    await proc.WaitForExitAsync();
                    var output = await proc.StandardOutput.ReadToEndAsync();

                    WslAddress = output[(output.IndexOf("inet ") + 4)..output.IndexOf('/')].Trim();
                }

                listenPath = WslAddress + ":5000";
            }
#endif

            if (listenPath.StartsWith('/')) {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.IP);
                var unixEp = new UnixDomainSocketEndPoint(listenPath);

                Logger.LogDebug($"Connecting to {listenPath}");
                await socket.ConnectAsync(unixEp);
                stm = new NetworkStream(socket);

                disconnect = () => {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                };
            } else {
                var tcp = new TcpClient();

                var portRaw = listenPath.LastIndexOf(':');
                var port = Int32.Parse(listenPath[(portRaw + 1)..]);

                Logger.LogDebug($"Connecting to {listenPath[..portRaw]}:{port}");
                await tcp.ConnectAsync(listenPath[..portRaw], port);
                stm = tcp.GetStream();

                disconnect = tcp.Close;
            }

            await stm.WriteAsync(payload, CancellationToken);

            var start = new byte[56];

            var recv = await stm.ReadAsync(start, CancellationToken);

            Debug.Assert(recv == 56);
            Debug.Assert(Encoding.ASCII.GetString(start) == "Status: 200 OK\r\nContent-Type: text/xml\r\nContent-Length: ");

            var lenBuffer = new char[8]; // 99 MiB max

            recv = 0;
            while (true) {
                if (recv >= 8)
                    throw new Exception("Malformed data (content length too long)");

                var @byte = stm.ReadByte();
                if (@byte == -1)
                    throw new Exception("Got EOF while reading content length");

                var chr = (char)@byte;

                if (chr == '\r') {
                    _ = stm.ReadByte(); // \n
                    _ = stm.ReadByte(); // \r
                    _ = stm.ReadByte(); // \n
                    recv = -2;
                    break;
                }

                lenBuffer[recv++] = chr;
            }

            if (recv != -2)
                throw new Exception("Content length reading did not complete");

            var len = Int32.Parse(lenBuffer);
            var cursor = 0;
            Memory<byte> buffer = new byte[len];
            while (cursor < len) {
                recv = stm.Read(buffer[cursor..].Span);
                if (recv == 0)
                    break;
                cursor += recv;
            }

            disconnect();

            return buffer;
        }
        
        
        
        public Task<XMLUtils.TorrentsResult[]> XmlActionTorrents(IList<byte[]> Hashes, params string[] Actions) =>
            XmlActionTorrentsMulti(Hashes.Select(x => (x, Array.Empty<string>())), Actions, (action, hash, reply) => reply == "0");

        public Task<XMLUtils.TorrentsResult[]> XmlActionTorrentsParam(IList<(byte[] Hash, string Param)> HashParams, string Action, Func<string, byte[], string, bool> FxExpectedReply) =>
            XmlActionTorrentsMulti(HashParams.Select(x => (x.Hash, new[] { x.Param })), new[] { Action }, FxExpectedReply);

        private async Task<XMLUtils.TorrentsResult[]> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, string[] Params)> HashParams, string[] Actions, Func<string, byte[], string, bool> FxExpectedReply)
        {
            var result = await XmlActionTorrentsMulti(HashParams, Actions);

            XMLUtils.SeekTo(ref result, XMLUtils.METHOD_RESPONSE);
            XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_START);

            var ret = new List<XMLUtils.TorrentsResult>();

            foreach (var (hash, _) in HashParams) {

                foreach (var action in Actions) {
                    if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                        // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
                        var status = XMLUtils.GetFaultStruct(ref result, action);
                        ret.Add(new XMLUtils.TorrentsResult(hash, [ status ]));

                        continue;
                    }

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                    var resp = XMLUtils.GetAnyValue(ref result);

                    if (FxExpectedReply(action, hash, resp)) {
                        ret.Add(new XMLUtils.TorrentsResult(hash, [ new XMLUtils.FaultStatus
                        {
                            Command = action,
                            FaultCode = "0",
                            FaultString = ""
                        } ]));
                    } else {
                        ret.Add(new XMLUtils.TorrentsResult(hash, [ new XMLUtils.FaultStatus
                        {
                            Command = action,
                            FaultCode = resp,
                            FaultString = "Unexpected response"
                        } ]));
                    }

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                }
            }

            return ret.ToArray();
        }

        public async Task<ReadOnlyMemory<byte>> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, string[] Params)> HashParams, string[] Actions)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var (hash, @params) in HashParams) {
                var sHash = Convert.ToHexString(hash);

                foreach (var action in Actions) {
                    xml.Append("<value><struct><member><name>methodName</name><value><string>" + action + "</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value>" + (@params.Length == 0 ? "" : String.Join("", @params.Select(x => $"<value>{x}</value>"))) + "</data></array></value></member></struct></value>");
                }
            }

            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Get(xml.ToString());

            return result;
        }

        public async Task<ReadOnlyMemory<byte>> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, (string Action, string[] Params)[] Actions)> In)
        {
            var xml = new StringBuilder();
            xml.Append("<?xml version=\"1.0\"?><methodCall><methodName>system.multicall</methodName><params><param><value><array><data>");
            foreach (var (hash, actions) in In) {
                var sHash = Convert.ToHexString(hash);

                foreach (var (action, @params) in actions) {
                    xml.Append("<value><struct><member><name>methodName</name><value><string>" + action + "</string></value></member><member><name>params</name><value><array><data><value><string>" + sHash + "</string></value>" + (@params.Length == 0 ? "" : String.Join("", @params.Select(x => $"<value>{x}</value>"))) + "</data></array></value></member></struct></value>");
                }
            }

            xml.Append("</data></array></value></param></params></methodCall>");

            var result = await Get(xml.ToString());

            return result;
        }
    }
}
