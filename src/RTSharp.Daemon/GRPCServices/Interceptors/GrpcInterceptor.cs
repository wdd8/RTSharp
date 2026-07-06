using Grpc.Core;
using Grpc.Core.Interceptors;

using RTSharp.Daemon.Exceptions;
using RTSharp.Daemon.Utils;

namespace RTSharp.Daemon.GRPCServices.Interceptors;

public class GrpcInterceptor(ILogger<GrpcInterceptor> Logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try {
            return await continuation(request, context);
        } catch (DataProviderFailure ex) {
            Logger.LogConnectionFailure(ex);
            throw new RpcException(new Status(StatusCode.Unavailable, ex.Message));
        } catch (OperationCanceledException ex) {
            // no one cares
            throw new RpcException(new Status(StatusCode.Cancelled, ex.Message));
        }
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(TRequest request, IServerStreamWriter<TResponse> responseStream, ServerCallContext context, ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try {
            await continuation(request, responseStream, context);
        } catch (DataProviderFailure ex) {
            Logger.LogConnectionFailure(ex);
            throw new RpcException(new Status(StatusCode.Unavailable, ex.Message));
        } catch (OperationCanceledException ex) {
            // no one cares
            throw new RpcException(new Status(StatusCode.Cancelled, ex.Message));
        }
    }
}
