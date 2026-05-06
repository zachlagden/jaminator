using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Jaminator.Services
{
    /// <summary>
    /// Self-update via the MSI release asset. We download the new MSI, then
    /// hand off to msiexec — Windows' own MajorUpgrade flow swaps files,
    /// re-registers the scheduled task, and updates Add/Remove Programs.
    /// </summary>
    public sealed class SelfUpdater
    {
        private const string ReleasesApi = "https://api.github.com/repos/zachlagden/jaminator/releases/latest";
        private const string MsiAssetName = "Jaminator.msi";

        private static readonly HttpClient Http = new HttpClient
        {
            DefaultRequestHeaders =
            {
                UserAgent = { ProductInfoHeaderValue.Parse("Jaminator/1.0") },
                Accept = { MediaTypeWithQualityHeaderValue.Parse("application/vnd.github+json") }
            },
            Timeout = TimeSpan.FromSeconds(60)
        };

        private readonly Logger _log;
        public SelfUpdater(Logger log) { _log = log; }

        public async Task<UpdateInfo?> CheckAsync(string currentVersion)
        {
            try
            {
                var json = await Http.GetStringAsync(ReleasesApi);
                var doc = JObject.Parse(json);
                var tag = (string?)doc["tag_name"] ?? "";
                var name = (string?)doc["name"] ?? tag;
                var version = tag.TrimStart('v');

                if (string.IsNullOrEmpty(version)) return null;
                if (CompareSemver(version, currentVersion) <= 0) return null;

                string? msiUrl = null;
                foreach (var a in doc["assets"] ?? new JArray())
                {
                    var n = (string?)a["name"] ?? "";
                    if (string.Equals(n, MsiAssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        msiUrl = (string?)a["browser_download_url"];
                        break;
                    }
                }
                if (string.IsNullOrEmpty(msiUrl))
                {
                    _log.Warn($"Latest release {tag} has no {MsiAssetName} asset — skipping update");
                    return null;
                }

                return new UpdateInfo(version, name, msiUrl!);
            }
            catch (Exception ex)
            {
                _log.Warn("Self-update check failed: " + ex.Message);
                return null;
            }
        }

        public async Task<bool> ApplyAsync(UpdateInfo info)
        {
            try
            {
                var msiPath = Path.Combine(Path.GetTempPath(), $"Jaminator-{info.Version}.msi");

                _log.Info($"Downloading update {info.Version} from {info.DownloadUrl}");
                using (var resp = await Http.GetAsync(info.DownloadUrl))
                {
                    resp.EnsureSuccessStatusCode();
                    using var fs = File.Create(msiPath);
                    await resp.Content.CopyToAsync(fs);
                }
                _log.Info($"Downloaded to {msiPath}");

                // Hand off to msiexec. /qb gives a tiny progress bar so the user knows
                // something's happening; /qn would be totally silent. MajorUpgrade in
                // the .msi handles the file swap + re-runs custom actions.
                var psi = new ProcessStartInfo("msiexec.exe",
                    $"/i \"{msiPath}\" /qb /norestart /L*V \"{msiPath}.log\"")
                {
                    UseShellExecute = true,
                    Verb = "runas" // ensure elevation if not already
                };
                Process.Start(psi);

                _log.Info("msiexec launched — exiting so it can replace files");
                return true;
            }
            catch (Exception ex)
            {
                _log.Error("Self-update failed", ex);
                return false;
            }
        }

        private static int CompareSemver(string a, string b)
        {
            try
            {
                if (Version.TryParse(Pad(a), out var va) && Version.TryParse(Pad(b), out var vb))
                    return va.CompareTo(vb);
            }
            catch { }
            return string.CompareOrdinal(a, b);
        }
        private static string Pad(string v)
        {
            var parts = v.Split('-')[0].Split('.');
            return parts.Length switch { 1 => $"{v}.0.0.0", 2 => $"{v}.0.0", 3 => $"{v}.0", _ => v };
        }
    }

    public sealed record UpdateInfo(string Version, string Title, string DownloadUrl);
}
