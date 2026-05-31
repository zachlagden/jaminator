using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Jaminator;
using Jaminator.Models;
using Newtonsoft.Json;

namespace Jaminator.Services
{
    /// <summary>
    /// Fetches manifest.json from GitHub (public) and joins it with secrets.json
    /// (private GitHub repo, PAT-gated) into a single in-memory Manifest. Both
    /// files cache to %ProgramData%\Jaminator\cache\ atomically as a pair, and
    /// the joined cached pair is the offline-resilient fallback for logon-time
    /// runs without network.
    /// </summary>
    internal sealed class ManifestFetcher
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static ManifestFetcher()
        {
            // D-14: every request carries a User-Agent. The private fetch sets one
            // per-request on its HttpRequestMessage; the public GetStringAsync path
            // has no per-request message, so the shared client needs a default UA
            // (GitHub rejects UA-less API requests; raw content tolerates it, but the
            // locked decision requires UA on both).
            Http.DefaultRequestHeaders.UserAgent.ParseAdd($"Jaminator/{Program.ToolVersion}");
        }

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

        private static string SecretsCachePath
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                    "Jaminator", "cache");
                Directory.CreateDirectory(dir);
                return Path.Combine(dir, "secrets.json");
            }
        }

        /// <summary>
        /// Fresh dual fetch (public manifest + private secrets) joined in memory by SSID;
        /// falls back to the joined cached pair on network failure. <c>fromCache: true</c>
        /// only when BOTH files were served from cache (D-12).
        /// </summary>
        public async Task<(Manifest manifest, bool fromCache)> FetchAsync(string url)
        {
            try
            {
                // Public fetch (D-09: sequential, public-first).
                var bust = $"?t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var publicJson = await Http.GetStringAsync(url + bust).ConfigureAwait(false);
                var manifest = JsonConvert.DeserializeObject<Manifest>(publicJson)
                        ?? throw new InvalidOperationException("Public manifest deserialised to null");

                // Private fetch (D-09 sequential; D-13 per-request bearer; D-14 User-Agent).
                // BuildSecrets.SecretsUrl already carries ?ref=<branch>, so the cache-buster
                // must be `&t=...` not `?t=...` (RESEARCH.md line 653).
                var secretsUrl = BuildSecrets.SecretsUrl + $"&t={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
                var secretsJson = await FetchSecretsWithBearerAsync(
                    secretsUrl, BuildSecrets.WifiPat, $"Jaminator/{Program.ToolVersion}").ConfigureAwait(false);
                var secrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)
                        ?? throw new InvalidOperationException("Private secrets deserialised to null");

                // Join in memory (D-10). Unmatched SSIDs leave Psk == null — the Phase 3
                // runner skips them with a clear log message.
                JoinPsks(manifest, secrets);

                // Atomic-pair cache write (D-10). Best-effort — a cache failure must not
                // crash the fetch path; the next launch will either re-fetch (online) or
                // detect corruption via the deserialise-null check on the cache-read side.
                try { WriteCachedPair(publicJson, secretsJson); } catch { /* cache write best-effort */ }

                return (manifest, fromCache: false);
            }
            catch (Exception netEx)
            {
                // Joined-cache fallback (D-11). BOTH cached files must exist; otherwise
                // fall through to the no-cache throw — no half-from-cache state (D-12).
                if (File.Exists(CachePath) && File.Exists(SecretsCachePath))
                {
                    try
                    {
                        var publicJson = File.ReadAllText(CachePath);
                        var manifest = JsonConvert.DeserializeObject<Manifest>(publicJson)
                                ?? throw new InvalidOperationException("Cached public manifest deserialised to null");

                        var secretsJson = File.ReadAllText(SecretsCachePath);
                        var secrets = JsonConvert.DeserializeObject<Dictionary<string, string>>(secretsJson)
                                ?? throw new InvalidOperationException("Cached private secrets deserialised to null");

                        JoinPsks(manifest, secrets);
                        return (manifest, fromCache: true);
                    }
                    catch (Exception cacheEx)
                    {
                        throw new InvalidOperationException(
                            $"Network fetch failed ({netEx.Message}) and joined cache is corrupt ({cacheEx.Message})", netEx);
                    }
                }
                throw new InvalidOperationException(
                    $"Network fetch failed and no joined cached pair exists: {netEx.Message}", netEx);
            }
        }

        /// <summary>
        /// Per-request bearer-auth fetch on the shared static HttpClient. The
        /// HttpRequestMessage scopes the Authorization header to this single call
        /// (D-13) — the public fetch must never see the PAT. Targets the GitHub
        /// REST API contents endpoint with Accept: application/vnd.github.raw+json
        /// (RESEARCH.md Pattern 1 / Pitfall 1 — the raw GitHub user-content host
        /// silently ignores auth on private repos, so we never use it here).
        /// </summary>
        private static async Task<string> FetchSecretsWithBearerAsync(string url, string bearerToken, string userAgent)
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.raw+json"));
            req.Headers.UserAgent.ParseAdd(userAgent);
            req.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// SSID→PSK lookup. Profiles whose SSID has no key in the secrets map
        /// retain <c>Psk == null</c> (D-10). Do NOT default to empty string —
        /// would be misread as "open network" by the Phase 3 runner.
        /// </summary>
        private static void JoinPsks(Manifest manifest, Dictionary<string, string> secrets)
        {
            if (manifest.Wifi == null) return;
            foreach (var profile in manifest.Wifi.Profiles)
            {
                if (secrets.TryGetValue(profile.Ssid, out var psk))
                {
                    profile.Psk = psk;
                }
                // Else: leave Psk as null. The Phase 3 runner is responsible for
                // skipping entries with no PSK and logging a clear message — do NOT
                // default to an empty string here (would be misread as "open network").
            }
        }

        /// <summary>
        /// Atomic-pair cache write of both fetched JSON bodies. .NET 4.8 lacks the
        /// three-arg File.Move overload that accepts an overwrite flag (added in
        /// .NET Core 3.0), so we Delete-then-Move per file. The brief
        /// between-the-two-Moves window
        /// where manifest is new and secrets is old is documented and accepted for
        /// v0.8.0 — HARDEN-06 (M3) will revisit with parallel I/O redesign.
        /// </summary>
        private static void WriteCachedPair(string publicJson, string secretsJson)
        {
            var manifestPath = CachePath;
            var secretsPath = SecretsCachePath;
            var manifestTmp = manifestPath + ".tmp";
            var secretsTmp = secretsPath + ".tmp";

            try
            {
                File.WriteAllText(manifestTmp, publicJson);
                File.WriteAllText(secretsTmp, secretsJson);

                if (File.Exists(manifestPath)) File.Delete(manifestPath);
                File.Move(manifestTmp, manifestPath);
                if (File.Exists(secretsPath)) File.Delete(secretsPath);
                File.Move(secretsTmp, secretsPath);
            }
            finally
            {
                // secrets.json.tmp holds plaintext SSID→PSK. A partial failure
                // (disk full, ACL denial) between the writes and the Moves would
                // otherwise strand it in %ProgramData% indefinitely. After a
                // successful Move the .tmp no longer exists, so these are no-ops.
                try { if (File.Exists(manifestTmp)) File.Delete(manifestTmp); } catch { /* best-effort cleanup */ }
                try { if (File.Exists(secretsTmp)) File.Delete(secretsTmp); } catch { /* best-effort cleanup */ }
            }
        }
    }
}
