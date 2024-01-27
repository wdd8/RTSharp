using Google.Protobuf.WellKnownTypes;

using Grpc.Core;

using RTSharp.Auxiliary.Protocols;

namespace RTSharp.Auxiliary.Services
{
    public class ServerService(ILogger<ServerService> Logger) : GRPCServerService.GRPCServerServiceBase
    {
        public override Task<Empty> Test(Empty request, ServerCallContext context) => Task.FromResult(new Empty());
    }
}