using QBittorrent.Client;

using RTSharp.Daemon.Exceptions;

using System.Net.Sockets;

namespace RTSharp.Daemon.Utils;

public static class ConnectionLog
{
    public static bool IsConnectionFailure(this Exception Ex) =>
        Ex is SocketException
        or HttpRequestException
        or Transmission.Net.Exception.TransmissionException { InnerException: HttpRequestException } ||
        (Ex is TaskCanceledException { InnerException: TimeoutException }) ||
        (Ex is QBittorrentClientRequestException qbitEx && (qbitEx.HttpRequestError == HttpRequestError.ConnectionError || qbitEx.StatusCode == System.Net.HttpStatusCode.Forbidden));

    public static void LogConnectionFailure(this ILogger Logger, DataProviderFailure Ex)
    {
        Logger.LogError("Failed to connect to {destination}: {reason}", Ex.Destination, Ex.Message);
        Logger.LogDebug(Ex, "Failed to connect to {destination}", Ex.Destination);
    }
}
