using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows.Forms;
using WixToolset.Dtf.WindowsInstaller;

namespace Jaminator.UpdateCheck
{
    /// <summary>
    /// Custom action that runs at the start of the install. Probes GitHub for the
    /// latest release; if it's newer than the MSI being executed, downloads that
    /// MSI to %TEMP%, spawns msiexec on it, and aborts the current install.
    /// </summary>
    public static class UpdateCheckCA
    {
        private const string ReleasesApi =
            "https://api.github.com/repos/zachlagden/jaminator/releases/latest";

        [CustomAction]
        public static ActionResult CheckForNewerVersion(Session session)
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                var current = session["ProductVersion"];
                session.Log($"UpdateCheck: this MSI is v{current}");

                // 5s probe: if GitHub is unreachable, fail open and let install proceed.
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                http.DefaultRequestHeaders.UserAgent.Add(
                    ProductInfoHeaderValue.Parse("JaminatorMsiCA/1.0"));
                http.DefaultRequestHeaders.Accept.Add(
                    MediaTypeWithQualityHeaderValue.Parse("application/vnd.github+json"));

                string json;
                try { json = http.GetStringAsync(ReleasesApi).GetAwaiter().GetResult(); }
                catch (Exception ex)
                {
                    session.Log($"UpdateCheck: skipped — couldn't reach GitHub ({ex.Message})");
                    return ActionResult.Success;
                }

                var tag = ExtractField(json, "tag_name");
                if (string.IsNullOrEmpty(tag))
                {
                    session.Log("UpdateCheck: skipped — no tag_name in GitHub response");
                    return ActionResult.Success;
                }
                var latest = tag!.TrimStart('v');

                if (CompareVersions(latest, current) <= 0)
                {
                    session.Log($"UpdateCheck: already on or newer than latest ({tag})");
                    return ActionResult.Success;
                }

                var msiUrl = FindAssetUrl(json, "Jaminator.msi");
                if (msiUrl == null)
                {
                    session.Log("UpdateCheck: latest release has no Jaminator.msi asset, continuing");
                    return ActionResult.Success;
                }

                // Only prompt in interactive mode; silent installs (/qn) just take the newer.
                var uiLevel = TryGetIntProperty(session, "UILevel", 0);
                var interactive = uiLevel >= 3;

                if (interactive)
                {
                    var ans = MessageBox.Show(
                        $"A newer Jaminator is available.\n\n" +
                        $"  This installer:  v{current}\n" +
                        $"  Latest release:  v{latest}\n\n" +
                        "Download and install the newer version instead?",
                        "Jaminator — newer version available",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information,
                        MessageBoxDefaultButton.Button1);
                    if (ans != DialogResult.Yes)
                    {
                        session.Log("UpdateCheck: user declined upgrade, continuing with bundled version");
                        return ActionResult.Success;
                    }
                }

                var msiPath = Path.Combine(Path.GetTempPath(), $"Jaminator-{latest}.msi");
                session.Log($"UpdateCheck: downloading {msiUrl}");
                using (var resp = http.GetAsync(msiUrl, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
                {
                    resp.EnsureSuccessStatusCode();
                    using var fs = File.Create(msiPath);
                    resp.Content.CopyToAsync(fs).GetAwaiter().GetResult();
                }
                session.Log($"UpdateCheck: saved to {msiPath}");

                // Hand off to a fresh msiexec so we don't conflict with our own running session.
                var psi = new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                session.Log("UpdateCheck: launched newer MSI; aborting this install");

                // UserExit closes the current MSI without an error dialog.
                return ActionResult.UserExit;
            }
            catch (Exception ex)
            {
                session.Log($"UpdateCheck: unexpected error, failing open — {ex}");
                return ActionResult.Success;
            }
        }

        // --- helpers ---

        private static int TryGetIntProperty(Session s, string name, int fallback)
        {
            var v = s[name];
            return int.TryParse(v, out var n) ? n : fallback;
        }

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
            const string urlMarker = "\"browser_download_url\":\"";
            var idx = 0;
            while ((idx = json.IndexOf(urlMarker, idx, StringComparison.Ordinal)) >= 0)
            {
                var start = idx + urlMarker.Length;
                var end = json.IndexOf('"', start);
                if (end < 0) return null;
                var url = json.Substring(start, end - start);
                if (url.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase) ||
                    url.EndsWith(assetName, StringComparison.OrdinalIgnoreCase))
                    return url;
                idx = end;
            }
            return null;
        }

        private static int CompareVersions(string a, string b)
        {
            if (Version.TryParse(Pad(a), out var va) && Version.TryParse(Pad(b), out var vb))
                return va.CompareTo(vb);
            return string.CompareOrdinal(a, b);
        }

        private static string Pad(string v)
        {
            var clean = v.Split('-', '+')[0];
            var parts = clean.Split('.');
            return parts.Length switch
            {
                1 => $"{clean}.0.0.0",
                2 => $"{clean}.0.0",
                3 => $"{clean}.0",
                _ => clean
            };
        }
    }
}
