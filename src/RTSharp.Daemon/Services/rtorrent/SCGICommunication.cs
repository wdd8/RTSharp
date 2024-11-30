using System.Diagnostics;
using System.Net.Sockets;
using System.Text;

namespace RTSharp.Daemon.Services.rtorrent
{
    public class SCGICommunication
    {
        private IConfiguration Config { get; }
        public ILogger<SCGICommunication> Logger { get; }

        public SCGICommunication(IConfiguration Config, ILogger<SCGICommunication> Logger)
        {
            this.Config = Config;
            this.Logger = Logger;
        }

#if DEBUG
        private static string WslAddress;
#endif

        public async Task<ReadOnlyMemory<byte>> Get(string Xml, CancellationToken CancellationToken = default)
        {
            var payload = SCGIPayloadBuilder.BuildPayload(Xml);
            NetworkStream stm;
            var listenPath = Config.GetSection("DataProviders:rtorrent:SCGIListen").Value;
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
    }
}
