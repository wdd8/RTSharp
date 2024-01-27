using System;
using System.Net;

namespace RTSharp.Core.Services.Auxiliary
{
    public static class Extensions
    {
        public static Uri GetUri(this Server Server)
        {
            string mid;
            if (IPAddress.TryParse(Server.Host, out var address)) {
                mid = (new IPEndPoint(address, Server.AuxiliaryServicePort)).ToString();
            } else {
                mid = Server.Host + ":" + Server.AuxiliaryServicePort;
            }

            return new Uri("https://" + mid + "/");
        }
    }
}
