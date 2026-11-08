using System;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using QBittorrent.Client.Internal;

namespace QBittorrent.Client.Extensions
{
    internal static class HttpClientExtensions
    {
        public static async Task<T> GetJsonWithCancellationAsync<T>(this HttpClient client,
            Uri uri,
            CancellationToken token)
        {
            using var response = await client.GetAsync(uri, token).ConfigureAwait(false);
            response.EnsureSuccessStatusCodeEx();
            return await response.Content.ReadJsonAsync<T>(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Like <see cref="GetJsonWithCancellationAsync{T}"/>, but a resource that isn't found reads as <see langword="default"/>.
        /// </summary>
        public static async Task<T> GetJsonOrDefaultWithCancellationAsync<T>(this HttpClient client,
            Uri uri,
            CancellationToken token)
        {
            using var response = await client.GetAsync(uri, token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return default;

            response.EnsureSuccessStatusCodeEx();
            return await response.Content.ReadJsonAsync<T>(token: token).ConfigureAwait(false);
        }

        /// <summary>
        /// Deserializes the content straight from its stream, with <see cref="QBittorrentJson.Options"/> when no options are given.
        /// Empty content reads as <see langword="default"/>.
        /// </summary>
        public static async Task<T> ReadJsonAsync<T>(this HttpContent content, JsonSerializerOptions options = null, CancellationToken token = default)
        {
            await using var stream = await content.ReadAsStreamAsync(token).ConfigureAwait(false);
            if (stream.CanSeek && stream.Length == 0)
                return default;

            return await JsonSerializer.DeserializeAsync<T>(stream, options ?? QBittorrentJson.Options, token).ConfigureAwait(false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Task<string> GetStringWithCancellationAsync(this HttpClient client, 
            Uri uri, 
            CancellationToken token)
        {
            return client.GetStringWithCancellationAsync(uri, false, token);
        }
        
        public static async Task<string> GetStringWithCancellationAsync(this HttpClient client, 
            Uri uri, 
            bool returnEmptyIfNotFound, 
            CancellationToken token)
        {
            using (var response = await client.GetAsync(uri, token).ConfigureAwait(false))
            {
                if (returnEmptyIfNotFound && response.StatusCode == HttpStatusCode.NotFound)
                    return string.Empty;
                
                response.EnsureSuccessStatusCodeEx();
                HttpContent content = response.Content;
                if (content != null)
                {
                    return await content.ReadAsStringAsync().ConfigureAwait(false);
                }

                return string.Empty;
            }
        }

        public static void EnsureSuccessStatusCodeEx(this HttpResponseMessage message)
        {
            try
            {
                // Call original EnsureSuccessStatusCode to get the same behavior and retrieve the error message.
                message.EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                throw new QBittorrentClientRequestException(ex.Message, message.StatusCode);
            }
        }
    }
}
