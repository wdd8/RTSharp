using System.Linq;
using System.Text.Json;

namespace QBittorrent.Client.Internal
{
    internal static class RssParser
    {
        internal static RssFolder Parse(JsonElement root)
        {
            return (RssFolder)Convert(root, string.Empty);

            RssItem ConvertFeed(JsonElement element) => element.Deserialize<RssFeed>(QBittorrentJson.RssOptions);

            RssItem ConvertFolder(JsonElement element) => new RssFolder(element.EnumerateObject().Select(p => Convert(p.Value, p.Name)));

            RssItem Convert(JsonElement element, string name)
            {
                var item = element.TryGetProperty("uid", out _) ? ConvertFeed(element) : ConvertFolder(element);
                item.Name = name;
                return item;
            }
        }
    }
}
