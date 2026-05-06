using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Jaminator.Services
{
    public sealed class Downloader
    {
        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private readonly Logger _log;

        public Downloader(Logger log) { _log = log; }

        /// <summary>
        /// Downloads the URL to <paramref name="targetPath"/> and verifies the sha256 if provided.
        /// Throws if the hash mismatches.
        /// </summary>
        public async Task DownloadVerifiedAsync(string url, string targetPath, string? expectedSha256)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            var tmp = targetPath + ".part";

            _log.Info($"Downloading {url}");
            using (var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                using var fs = File.Create(tmp);
                await resp.Content.CopyToAsync(fs);
            }

            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !expectedSha256!.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            {
                if (!HashVerifier.Matches(tmp, expectedSha256))
                {
                    var actual = HashVerifier.Sha256OfFile(tmp);
                    File.Delete(tmp);
                    throw new InvalidOperationException(
                        $"Hash mismatch for {url}. Expected {expectedSha256}, got {actual}.");
                }
                _log.Info("  sha256 verified");
            }
            else
            {
                _log.Warn("  sha256 skipped (placeholder or empty)");
            }

            if (File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(tmp, targetPath);
            _log.Info($"  saved to {targetPath}");
        }
    }
}
