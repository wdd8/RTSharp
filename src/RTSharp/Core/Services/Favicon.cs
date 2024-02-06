using System.Net.Http;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Serilog;

namespace RTSharp.Core.Services
{
    public class Favicon
    {
        private readonly HttpClient HttpClient;

        public Favicon(HttpClient HttpClient)
        {
            this.HttpClient = HttpClient;
        }

        public async Task<byte[]?> GetFavicon(string Domain)
        {
            var cts = new CancellationTokenSource();
            cts.CancelAfter(5000);

            try {
                /*var response = await HttpClient.GetAsync($"https://www.google.com/s2/favicons?domain_url={HttpUtility.UrlEncode(Domain)}", cts.Token);

                if (!response.IsSuccessStatusCode)
                    return null;

                return await response.Content.ReadAsByteArrayAsync();*/
                var response = await HttpClient.GetAsync($"https://{Domain}/favicon.ico", cts.Token);

                if (response.IsSuccessStatusCode)
                    return await response.Content.ReadAsByteArrayAsync();

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
                    response = await HttpClient.GetAsync(links.First(), cts.Token);
                    if (response.IsSuccessStatusCode)
                        return await response.Content.ReadAsByteArrayAsync();
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
