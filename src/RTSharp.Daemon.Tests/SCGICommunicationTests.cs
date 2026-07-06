using System.Net;
using System.Net.Sockets;
using System.Text;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using RTSharp.Daemon.Services.rtorrent;

using Xunit;

namespace RTSharp.Daemon.Tests;

public class SCGICommunicationTests
{
    private sealed class FixedOptionsFactory<T>(T Value) : IOptionsFactory<T> where T : class
    {
        public T Create(string name) => Value;
    }

    private static async Task RunLoopbackServer(TcpListener Listener, byte[] Body, CancellationToken CancellationToken)
    {
        using var client = await Listener.AcceptTcpClientAsync(CancellationToken);
        using var stream = client.GetStream();

        // reset
        var discard = new byte[4096];
#pragma warning disable CA2022 // Avoid inexact read with 'Stream.Read'
        await stream.ReadAsync(discard, CancellationToken);
#pragma warning restore CA2022 // Avoid inexact read with 'Stream.Read'

        var header = Encoding.ASCII.GetBytes($"Status: 200 OK\r\nContent-Type: text/xml\r\nContent-Length: {Body.Length}\r\n\r\n");

        foreach (var chunk in header.Chunk(3)) {
            await stream.WriteAsync(chunk, CancellationToken);
            await Task.Delay(1, CancellationToken);
        }

        foreach (var chunk in Body.Chunk(7)) {
            await stream.WriteAsync(chunk, CancellationToken);
            await Task.Delay(1, CancellationToken);
        }
    }

    [Fact]
    public async Task Get_ParsesHeaderAndBody_WhenDeliveredAsFragmentedNetworkWrites()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var bodyXml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<methodResponse>\r\n<params>\r\n<param><value><string>hello</string></value></param>\r\n</params>\r\n</methodResponse>\r\n";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyXml);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var serverTask = RunLoopbackServer(listener, bodyBytes, cts.Token);

        var comm = new SCGICommunication(
            new FixedOptionsFactory<ConfigModel>(new ConfigModel { SCGIListen = $"127.0.0.1:{port}" }),
            "test",
            NullLogger<SCGICommunication>.Instance);

        using var response = await comm.Get("<?xml version=\"1.0\"?><methodCall><methodName>test</methodName><params></params></methodCall>", cts.Token);
        var reader = response.Reader();

        XMLUtils.SeekTo(ref reader, XMLUtils.METHOD_RESPONSE);
        XMLUtils.SeekFixed(ref reader, XMLUtils.SINGLE_PARAM);
        var value = XMLUtils.GetValue<string>(ref reader);

        Assert.Equal("hello", value);

        await serverTask;
        listener.Stop();
    }
}
