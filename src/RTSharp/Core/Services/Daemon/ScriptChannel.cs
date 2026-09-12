using Grpc.Core;

using RTSharp.Daemon.Protocols;
using RTSharp.Shared.Abstractions;
using RTSharp.Shared.Utils;

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

using ProtocolScriptMessage = RTSharp.Daemon.Protocols.ScriptMessage;
using ScriptMessage = RTSharp.Shared.Utils.ScriptMessage;

namespace RTSharp.Core.Services.Daemon;

public class ScriptChannel : IScriptChannel
{
    private readonly AsyncDuplexStreamingCall<ScriptChannelRequest, ProtocolScriptMessage> Call;

    private readonly Channel<ScriptMessage> RX = Channel.CreateUnbounded<ScriptMessage>(new UnboundedChannelOptions {
        SingleReader = false,
        SingleWriter = true
    });

    private readonly Task Attach;

    public ChannelReader<ScriptMessage> Output => RX.Reader;

    public ScriptChannel(AsyncDuplexStreamingCall<ScriptChannelRequest, ProtocolScriptMessage> Call, Guid SessionId, CancellationToken CancellationToken)
    {
        this.Call = Call;

        Attach = Call.RequestStream.WriteAsync(new ScriptChannelRequest {
            Attach = SessionId.ToByteString()
        }, CancellationToken);

        _ = Task.Run(async () => {
            try {
                await Attach;

                await foreach (var message in Call.ResponseStream.ReadAllAsync(CancellationToken))
                    await RX.Writer.WriteAsync(message.ToScriptMessage(), CancellationToken);
            } catch (Exception ex) when (ex is OperationCanceledException || ex is RpcException { StatusCode: StatusCode.Cancelled }) {
            } finally {
                RX.Writer.TryComplete();
            }
        }, CancellationToken);
    }

    public async ValueTask<T?> Read<T>(CancellationToken CancellationToken = default) where T : class, IScriptMessage<T>
    {
        await foreach (var message in RX.Reader.ReadAllAsync(CancellationToken)) {
            if (message.TryUnpack<T>(out var value))
                return value;
        }

        return null;
    }

    public async ValueTask Send<T>(T Message) where T : IScriptMessage<T>
    {
        await Attach;
        await Call.RequestStream.WriteAsync(new ScriptChannelRequest {
            Input = ScriptMessage.Of(Message).ToProtocol()
        });
    }

    public async ValueTask DisposeAsync()
    {
        try {
            await Call.RequestStream.CompleteAsync();
        } catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException || ex is RpcException { StatusCode: StatusCode.Cancelled }) {
        }

        Call.Dispose();
        GC.SuppressFinalize(this);
    }
}
