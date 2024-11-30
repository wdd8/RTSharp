using System;
using System.Net;

namespace RTSharp.Core.Services.Daemon
{
    public static class Extensions
    {
        public static Uri GetUri(this Config.Models.Server Server)
        {
            string mid;
            if (IPAddress.TryParse(Server.Host, out var address)) {
                mid = (new IPEndPoint(address, Server.DaemonPort)).ToString();
            } else {
                mid = Server.Host + ":" + Server.DaemonPort;
            }

            return new Uri("https://" + mid + "/");
        }
    }
}
