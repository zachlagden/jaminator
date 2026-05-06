using System;
using System.Net.Http;
using System.Threading.Tasks;
using Jaminator.Models;
using Newtonsoft.Json;

namespace Jaminator.Services
{
    internal sealed class ManifestFetcher
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };

        public async Task<Manifest> FetchAsync(string url)
        {
            // Cache-bust to dodge any CDN edge cache when iterating
            var bust = $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var json = await Http.GetStringAsync(url + bust).ConfigureAwait(false);
            var manifest = JsonConvert.DeserializeObject<Manifest>(json)
                           ?? throw new InvalidOperationException("Manifest deserialised to null");
            return manifest;
        }
    }
}
