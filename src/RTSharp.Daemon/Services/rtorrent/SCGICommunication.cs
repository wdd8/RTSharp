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
        /// Owns the pooled buffer holding the response body for the lifetime of the caller's parse. Dispose only after
        /// <see cref="Reader"/> is unused
        /// </summary>
        public readonly struct ScgiResponse : IDisposable
        {
            private readonly byte[] Rented;
            public readonly ReadOnlySequence<byte> Body;

            internal ScgiResponse(byte[] Rented, int Length)
            {
                this.Rented = Rented;
                Body = new ReadOnlySequence<byte>(Rented, 0, Length);
            }

            public SequenceReader<byte> Reader() => new SequenceReader<byte>(Body);

            public void Dispose() => ArrayPool<byte>.Shared.Return(Rented);
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
            var body = ArrayPool<byte>.Shared.Rent(Encoding.UTF8.GetByteCount(Xml));
            try {
                var length = Encoding.UTF8.GetBytes(Xml, body);
                return await Get(body.AsMemory(0, length), CancellationToken);
            } finally {
                ArrayPool<byte>.Shared.Return(body);
            }
        }

        public async Task<ScgiResponse> Get(ReadOnlyMemory<byte> XmlUtf8, CancellationToken CancellationToken = default)
        {
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

                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("Connecting to {ListenPath}", listenPath);
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
                var host = listenPath[..portRaw];
                var port = Int32.Parse(listenPath.AsSpan(portRaw + 1));

                if (Logger.IsEnabled(LogLevel.Debug))
                    Logger.LogDebug("Connecting to {Host}:{Port}", host, port);
                try {
                    await tcp.ConnectAsync(host, port, CancellationToken);
                } catch (SocketException ex) {
                    throw new DataProviderFailure(ex.Message, ex, $"{host}:{port}");
                }
                tcp.NoDelay = true;
                stm = tcp.GetStream();

                disconnect = tcp.Close;
            }

            var pipeReader = PipeReader.Create(stm, new StreamPipeReaderOptions(pool: MemoryPool<byte>.Shared, bufferSize: 64 * 1024, leaveOpen: true));
            byte[]? body = null;

            try {
                await stm.WriteAsync(SCGIPayloadBuilder.BuildHeader(XmlUtf8.Length), CancellationToken);
                await stm.WriteAsync(XmlUtf8, CancellationToken);

                var len = await ReadContentLengthAsync(pipeReader, CancellationToken);

                body = ArrayPool<byte>.Shared.Rent(len);
                var filled = 0;
                while (filled < len) {
                    var readResult = await pipeReader.ReadAsync(CancellationToken);
                    var buffer = readResult.Buffer;
                    var take = (int)Math.Min(buffer.Length, len - filled);

                    buffer.Slice(0, take).CopyTo(body.AsSpan(filled));
                    filled += take;
                    pipeReader.AdvanceTo(buffer.GetPosition(take));

                    if (filled < len && readResult.IsCompleted)
                        throw new Exception("Got EOF while reading SCGI response body");
                }

                return new ScgiResponse(body, len);
            } catch {
                if (body != null)
                    ArrayPool<byte>.Shared.Return(body);
                throw;
            } finally {
                await pipeReader.CompleteAsync();
                try {
                    disconnect();
                } catch {
                }
            }
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
                var statuses = new List<XMLUtils.FaultStatus>();

                foreach (var action in Actions) {
                    if (XMLUtils.GetValueType(result) == SCGI_DATA_TYPE.STRUCT) {
                        // <value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n<value><struct>\r\n<member><name>faultCode</name>\r\n<value><i4>-501</i4></value></member>\r\n<member><name>faultString</name>\r\n<value><string>Could not find info-hash.</string></value></member>\r\n</struct></value>\r\n</data></array></value></param>\r\n</params>\r\n</methodResponse>\r\n
                        statuses.Add(XMLUtils.GetFaultStruct(ref result, action));

                        continue;
                    }

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_START);
                    var resp = XMLUtils.GetAnyValue(ref result);

                    if (FxExpectedReply(action, hash, resp)) {
                        statuses.Add(new XMLUtils.FaultStatus {
                            Command = action,
                            FaultCode = "0",
                            FaultString = ""
                        });
                    } else {
                        statuses.Add(new XMLUtils.FaultStatus {
                            Command = action,
                            FaultCode = resp,
                            FaultString = "Unexpected response"
                        });
                    }

                    XMLUtils.SeekFixed(ref result, XMLUtils.MULTICALL_RESPONSE_ENTRY_END);
                }

                ret.Add(new XMLUtils.TorrentsResult(hash, [.. statuses]));
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
