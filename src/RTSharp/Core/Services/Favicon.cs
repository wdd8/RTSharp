using System.Net.Http;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using System.IO;

namespace RTSharp.Core.Services
{
    public class Favicon
    {
        private readonly HttpClient HttpClient;

        public Favicon(HttpClient HttpClient)
        {
            this.HttpClient = HttpClient;
        }

        public async Task<Stream?> GetFavicon(string Domain)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);

            try {
                /*var response = await HttpClient.GetAsync($"https://www.google.com/s2/favicons?domain_url={HttpUtility.UrlEncode(Domain)}", cts.Token);

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadAsStreamAsync();*/
                var response = await HttpClient.GetAsync($"https://{Domain}/favicon.ico", cts.Token);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsStreamAsync();

                var html = await HttpClient.GetStringAsync($"https://{Domain}/", cts.Token);

                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(html);
                var links = doc.DocumentNode.Descendants("link")
                    .Where(x => {
                        var rel = x.GetAttributeValue("rel", null);
                        return rel == "shortcut icon" || rel == "icon" || rel == "alternate icon";
                    }).Select(x => x.GetAttributeValue("href", null))
                    .Where(x => x != null)
                    .ToArray();

                if (links.Any()) {
                    var link = links.First();

                    if (!link.StartsWith("http")) {
                        if (!link.StartsWith('/'))
                            link = "/" + link;
                        link = $"https://{Domain}" + link;
                    }

                    response = await HttpClient.GetAsync(link, cts.Token);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsStreamAsync();
                }

                return null;
            } catch (TaskCanceledException) {
                return null;
            } catch (Exception ex) {
                Log.Logger.Error(ex, "Favicon fetch fail for domain \"" + Domain + "\"");
                return null;
            }
        }
    }
}
