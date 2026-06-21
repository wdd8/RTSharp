using Grpc.Core;

using RTSharp.Daemon.Protocols.DataProvider;
using RTSharp.Daemon.Services.FileTail;

using Status = Grpc.Core.Status;

namespace RTSharp.Daemon.Services.rtorrent
{
    public partial class Grpc
    {
        public Task StreamLogs(StreamLogsRequest Req, IServerStreamWriter<LogEntry> Res, CancellationToken CancellationToken)
        {
            var config = Opts.Create(InstanceKey);

            if (String.IsNullOrWhiteSpace(config.LogFile))
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "No log file configured for this rtorrent data provider (set LogFile to rtorrent's log.open_file path)."));

            return FileTailReader.StreamPath(config.LogFile, Req, Res, CancellationToken);
        }
    }
}
