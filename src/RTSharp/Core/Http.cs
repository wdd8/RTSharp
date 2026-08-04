using System.Net.Http;

namespace RTSharp.Core
{
    public static class Http
    {
        public static HttpClient Client = new HttpClient(new SocketsHttpHandler {
            Proxy = GlobalProxy.Instance,
            UseProxy = true
        });
    }
}
