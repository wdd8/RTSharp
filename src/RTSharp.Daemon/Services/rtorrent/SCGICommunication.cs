using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using RTSharp.Daemon.Exceptions;

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
        private static string? WslAddress;
#endif

        private static readonly byte[] EXPECTED_HEADER = "Status: 200 OK\r\nContent-Type: text/xml\r\nContent-Length: "u8.ToArray();

        /// <summary>
        /// Owns the connection's PipeReader for the lifetime of the caller's parse. Dispose only after all parsing
        /// against <see cref="Reader"/> results is complete - disposing recycles the pooled segments backing the body.
        /// </summary>
        public readonly struct ScgiResponse : IDisposable
        {
            private readonly PipeReader PipeReader;
            private readonly Action Disconnect;
            public readonly ReadOnlySequence<byte> Body;

            internal ScgiResponse(PipeReader PipeReader, Action Disconnect, ReadOnlySequence<byte> Body)
            {
                this.PipeReader = PipeReader;
                this.Disconnect = Disconnect;
                this.Body = Body;
            }

            public SequenceReader<byte> Reader() => new SequenceReader<byte>(Body);

            public void Dispose()
            {
                PipeReader.AdvanceTo(Body.End);
                PipeReader.Complete();
                Disconnect();
            }
        }

        private static async Task<int> ReadContentLengthAsync(PipeReader Reader, CancellationToken CancellationToken)
        {
            while (true) {
                var readResult = await Reader.ReadAsync(CancellationToken);
                var buffer = readResult.Buffer;
                var reader = new SequenceReader<byte>(buffer);

                if (reader.Remaining < EXPECTED_HEADER.Length) {
                    if (readResult.IsCompleted)
                        throw new Exception("Got EOF while reading SCGI response header");
                    Reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }

                Span<byte> headerBuf = stackalloc byte[EXPECTED_HEADER.Length];
                reader.TryCopyTo(headerBuf);
                Debug.Assert(headerBuf.SequenceEqual(EXPECTED_HEADER));
                reader.Advance(EXPECTED_HEADER.Length);

                if (!reader.TryReadTo(out ReadOnlySequence<byte> lenSeq, (byte)'\r', advancePastDelimiter: true)) {
                    if (readResult.IsCompleted)
                        throw new Exception("Got EOF while reading content length");
                    Reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }

                if (lenSeq.Length > 8)
                    throw new Exception("Malformed data (content length too long)");

                // Trailing \n\r\n after the \r already consumed above
                if (reader.Remaining < 3) {
                    if (readResult.IsCompleted)
                        throw new Exception("Got EOF while reading content length terminator");
                    Reader.AdvanceTo(buffer.Start, buffer.End);
                    continue;
                }
                reader.Advance(3);

                Span<byte> lenBuf = stackalloc byte[8];
                lenSeq.CopyTo(lenBuf);
                if (!Utf8Parser.TryParse(lenBuf[..(int)lenSeq.Length], out int len, out _))
                    throw new Exception("Malformed content length");

                Reader.AdvanceTo(reader.Position);
                return len;
            }
        }

        public async Task<ScgiResponse> Get(string Xml, CancellationToken CancellationToken = default)
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
                    await proc!.WaitForExitAsync();
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
                try {
                    await socket.ConnectAsync(unixEp, CancellationToken);
                } catch (SocketException ex) {
                    throw new DataProviderFailure(ex.Message, ex, listenPath);
                }
                stm = new NetworkStream(socket);

                disconnect = () => {
                    socket.Shutdown(SocketShutdown.Both);
                    socket.Close();
                };
            } else {
                var tcp = new TcpClient();

                var portRaw = listenPath.LastIndexOf(':');
                var port = Int32.Parse(listenPath[(portRaw + 1)..]);
                var destination = $"{listenPath[..portRaw]}:{port}";

                Logger.LogDebug($"Connecting to {destination}");
                try {
                    await tcp.ConnectAsync(listenPath[..portRaw], port, CancellationToken);
                } catch (SocketException ex) {
                    throw new DataProviderFailure(ex.Message, ex, destination);
                }
                stm = tcp.GetStream();

                disconnect = tcp.Close;
            }

            await stm.WriteAsync(payload, CancellationToken);

            var pipeReader = PipeReader.Create(stm, new StreamPipeReaderOptions(pool: MemoryPool<byte>.Shared, leaveOpen: true));

            int len;
            ReadResult bodyResult;
            try {
                len = await ReadContentLengthAsync(pipeReader, CancellationToken);
                bodyResult = await pipeReader.ReadAtLeastAsync(len, CancellationToken);
            } catch {
                await pipeReader.CompleteAsync();
                disconnect();
                throw;
            }

            var body = bodyResult.Buffer.Slice(0, len);

            return new ScgiResponse(pipeReader, disconnect, body);
        }



        public Task<XMLUtils.TorrentsResult[]> XmlActionTorrents(IList<byte[]> Hashes, params string[] Actions) =>
            XmlActionTorrentsMulti(Hashes.Select(x => (x, Array.Empty<string>())), Actions, (action, hash, reply) => reply == "0");

        public Task<XMLUtils.TorrentsResult[]> XmlActionTorrentsParam(IList<(byte[] Hash, string Param)> HashParams, string Action, Func<string, byte[], string, bool> FxExpectedReply) =>
            XmlActionTorrentsMulti(HashParams.Select(x => (x.Hash, new[] { x.Param })), new[] { Action }, FxExpectedReply);

        private async Task<XMLUtils.TorrentsResult[]> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, string[] Params)> HashParams, string[] Actions, Func<string, byte[], string, bool> FxExpectedReply)
        {
            using var response = await XmlActionTorrentsMulti(HashParams, Actions);
            var result = response.Reader();

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

        public async Task<ScgiResponse> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, string[] Params)> HashParams, string[] Actions)
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

            return await Get(xml.ToString());
        }

        public async Task<ScgiResponse> XmlActionTorrentsMulti(IEnumerable<(byte[] Hash, (string Action, string[] Params)[] Actions)> In)
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

            return await Get(xml.ToString());
        }
    }
}
