using RTSharp.Daemon.Protocols.DataProvider;

namespace RTSharp.Daemon.Services.rtorrent
{
    public static class GrpcExtensions
    {
        public static string ToFailureString(this Status In)
        {
            return $"{In.Command}: {In.FaultCode} {In.FaultString}";
        }

        public static string ToFailureString(this CommandReply In)
        {
            return String.Join('\n', In.Response.Select(x => x.ToFailureString()));
        }

        public static Status SuccessfulStatus(string Command = "")
        {
            return new Status() {
                Command = Command,
                FaultCode = "0",
                FaultString = ""
            };
        }
        
        public static Status? ToStatus(XMLUtils.FaultStatus? In)
        {
            if (In == null)
                return null;
            
            return new Status {
                Command = In.Command, 
                FaultCode = In.FaultCode,
                FaultString = In.FaultString
            };
        }
    }
}
