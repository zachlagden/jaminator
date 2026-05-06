using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Jaminator.Services
{
    public sealed class SelfUpdater
    {
        private const string ReleasesApi = "https://api.github.com/repos/zachlagden/jaminator/releases/latest";
        private const string AssetName = "Jaminator.exe";

        private static readonly HttpClient Http = new HttpClient
        {
            DefaultRequestHeaders =
            {
                UserAgent = { ProductInfoHeaderValue.Parse("Jaminator/1.0") },
                Accept = { MediaTypeWithQualityHeaderValue.Parse("application/vnd.github+json") }
            },
            Timeout = TimeSpan.FromSeconds(20)
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

                string? exeUrl = null;
                foreach (var a in doc["assets"] ?? new JArray())
                {
                    var n = (string?)a["name"] ?? "";
                    if (string.Equals(n, AssetName, StringComparison.OrdinalIgnoreCase))
                    {
                        exeUrl = (string?)a["browser_download_url"];
                        break;
                    }
                }
                if (string.IsNullOrEmpty(exeUrl)) return null;

                return new UpdateInfo(version, name, exeUrl!);
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
                var current = Process.GetCurrentProcess().MainModule!.FileName;
                var stagingPath = current + ".new";
                var oldPath = current + ".old";

                _log.Info($"Downloading update {info.Version} from {info.DownloadUrl}");
                using (var resp = await Http.GetAsync(info.DownloadUrl))
                {
                    resp.EnsureSuccessStatusCode();
                    using var fs = File.Create(stagingPath);
                    await resp.Content.CopyToAsync(fs);
                }

                // Stage replacement: relaunch a tiny script that swaps the EXE after we exit.
                var ps = $@"
$ErrorActionPreference = 'Stop'
Start-Sleep -Seconds 1
if (Test-Path '{oldPath}') {{ Remove-Item '{oldPath}' -Force }}
Move-Item -LiteralPath '{current}' -Destination '{oldPath}' -Force
Move-Item -LiteralPath '{stagingPath}' -Destination '{current}' -Force
Start-Process -FilePath '{current}'
";
                var scriptPath = Path.Combine(Path.GetTempPath(), "jaminator-update.ps1");
                File.WriteAllText(scriptPath, ps);

                Process.Start(new ProcessStartInfo("powershell.exe",
                    $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{scriptPath}\"")
                {
                    UseShellExecute = true
                });
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
