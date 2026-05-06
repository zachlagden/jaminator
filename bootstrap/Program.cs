using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;

namespace Jaminator.Bootstrap
{
    /// <summary>
    /// Self-updating downloader. Always fetches the latest Jaminator.msi from
    /// the GitHub release tagged "latest" and hands off to msiexec. Lets users
    /// download Jaminator-Setup.exe once and always end up with the current
    /// version — the EXE itself never needs updating.
    /// </summary>
    internal static class Program
    {
        private const string ReleasesApi =
            "https://api.github.com/repos/zachlagden/jaminator/releases/latest";

        private static int Main()
        {
            // Force TLS 1.2 — GitHub no longer accepts older protocols and
            // .NET Framework 4.8 sometimes negotiates down by default.
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Console.Title = "Jaminator Setup";
            Console.WriteLine("Jaminator Setup");
            Console.WriteLine("===============");
            Console.WriteLine();

            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                http.DefaultRequestHeaders.UserAgent.Add(
                    ProductInfoHeaderValue.Parse("JaminatorSetup/1.0"));
                http.DefaultRequestHeaders.Accept.Add(
                    MediaTypeWithQualityHeaderValue.Parse("application/vnd.github+json"));

                Console.Write("Querying GitHub for the latest release... ");
                var json = http.GetStringAsync(ReleasesApi).GetAwaiter().GetResult();
                Console.WriteLine("OK");

                var tag = ExtractField(json, "tag_name") ?? "unknown";
                Console.WriteLine($"  Tag: {tag}");

                var msiUrl = FindAssetUrl(json, "Jaminator.msi");
                if (msiUrl == null)
                {
                    Console.WriteLine();
                    Console.WriteLine("ERROR: This release has no Jaminator.msi asset.");
                    Console.WriteLine("Visit https://github.com/zachlagden/jaminator/releases");
                    Pause();
                    return 1;
                }

                var msiPath = Path.Combine(Path.GetTempPath(), $"Jaminator-{tag}.msi");
                Console.Write($"Downloading {SafeName(msiUrl)} ");
                Download(http, msiUrl, msiPath);
                Console.WriteLine(" OK");
                Console.WriteLine($"  Saved: {msiPath}");
                Console.WriteLine();

                Console.WriteLine("Launching Windows Installer...");
                var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine();
                Console.WriteLine("ERROR: " + ex.Message);
                Console.WriteLine();
                Console.WriteLine("If you have no internet, download Jaminator.msi directly from");
                Console.WriteLine("https://github.com/zachlagden/jaminator/releases");
                Pause();
                return 1;
            }
        }

        // --- tiny JSON helpers (avoid taking a Newtonsoft dep so the EXE stays small) ---

        private static string? ExtractField(string json, string fieldName)
        {
            var marker = $"\"{fieldName}\":\"";
            var i = json.IndexOf(marker, StringComparison.Ordinal);
            if (i < 0) return null;
            i += marker.Length;
            var end = json.IndexOf('"', i);
            return end < 0 ? null : json.Substring(i, end - i);
        }

        private static string? FindAssetUrl(string json, string assetName)
        {
            // Walk every "browser_download_url":"..." in the JSON and return the
            // one whose URL ends in the requested asset name (case-insensitive).
            const string urlMarker = "\"browser_download_url\":\"";
            var idx = 0;
            while ((idx = json.IndexOf(urlMarker, idx, StringComparison.Ordinal)) >= 0)
            {
                var start = idx + urlMarker.Length;
                var end = json.IndexOf('"', start);
                if (end < 0) return null;
                var url = json.Substring(start, end - start);
                if (url.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase)
                    || url.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                {
                    return url;
                }
                idx = end;
            }
            return null;
        }

        private static string SafeName(string url) =>
            Path.GetFileName(new Uri(url).AbsolutePath);

        private static void Download(HttpClient http, string url, string path)
        {
            using var resp = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead)
                                  .GetAwaiter().GetResult();
            resp.EnsureSuccessStatusCode();
            using var fs = File.Create(path);
            using var src = resp.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
            var buf = new byte[81920];
            long total = 0;
            int read;
            var lastDot = DateTime.UtcNow;
            while ((read = src.Read(buf, 0, buf.Length)) > 0)
            {
                fs.Write(buf, 0, read);
                total += read;
                if ((DateTime.UtcNow - lastDot).TotalMilliseconds > 250)
                {
                    Console.Write(".");
                    lastDot = DateTime.UtcNow;
                }
            }
            Console.Write($" ({total / 1024:N0} KB)");
        }

        private static void Pause()
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to close.");
            try { Console.ReadKey(true); } catch { }
        }
    }
}
