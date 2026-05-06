using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Jaminator.Models;
using Newtonsoft.Json;

namespace Jaminator.Services
{
    /// <summary>
    /// Fetches manifest.json from GitHub, with on-disk caching so logon-time
    /// runs survive flaky / absent network during a lesson. The most recent
    /// successful fetch is mirrored to ProgramData\Jaminator\cache\manifest.json
    /// and used as fallback when the network probe fails.
    /// </summary>
    internal sealed class ManifestFetcher
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        private static string CachePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Jaminator", "cache");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "manifest.json");
            }
        }

        /// <summary>Fresh fetch; falls back to last cached copy on network failure.</summary>
        public async Task<(Manifest manifest, bool fromCache)> FetchAsync(string url)
        {
            try
            {
                var bust = $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var json = await Http.GetStringAsync(url + bust).ConfigureAwait(false);
                var m = JsonConvert.DeserializeObject<Manifest>(json)
                        ?? throw new InvalidOperationException("Manifest deserialised to null");
                try { File.WriteAllText(CachePath, json); } catch { /* cache write best-effort */ }
                return (m, fromCache: false);
            }
            catch (Exception netEx)
            {
                if (File.Exists(CachePath))
                {
                    try
                    {
                        var json = File.ReadAllText(CachePath);
                        var m = JsonConvert.DeserializeObject<Manifest>(json)
                                ?? throw new InvalidOperationException("Cached manifest deserialised to null");
                        return (m, fromCache: true);
                    }
                    catch (Exception cacheEx)
                    {
                        throw new InvalidOperationException(
                            $"Network fetch failed ({netEx.Message}) and cache is corrupt ({cacheEx.Message})", netEx);
                    }
                }
                throw new InvalidOperationException(
                    $"Network fetch failed and no cached manifest exists: {netEx.Message}", netEx);
            }
        }
    }
}
