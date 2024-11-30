using RTSharp.DataProvider.Rtorrent.Protocols;
using RTSharp.DataProvider.Rtorrent.Protocols.Types;

namespace RTSharp.DataProvider.Rtorrent.Common;

public static class Extensions
{
    public static IList<(byte[], IList<Exception>)> ToExceptions(this TorrentsReply In)
    {
        return In.Torrents.Select(x => (
            x.InfoHash.ToByteArray(),
            (IList<Exception>)x.Status.Select(i => i.FaultCode != "0" ? new Exception(i.Command + ": FaultCode " + i.FaultCode + " - " + i.FaultString) : null).Where(x => x != null).ToList()
        )).ToList();
    }

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
}
