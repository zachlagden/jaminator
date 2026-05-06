using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jaminator.Models;
using Microsoft.Win32;

namespace Jaminator.Services
{
    public sealed class WallpaperSetter
    {
        private const int SPI_SETDESKWALLPAPER = 0x0014;
        private const int SPIF_UPDATEINIFILE = 0x01;
        private const int SPIF_SENDWININICHANGE = 0x02;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SystemParametersInfo(int uAction, int uParam, string lpvParam, int fuWinIni);

        private readonly Logger _log;
        private readonly Downloader _downloader;

        public WallpaperSetter(Logger log, Downloader downloader)
        {
            _log = log;
            _downloader = downloader;
        }

        public string LocalPath
        {
            get
            {
                var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                return Path.Combine(pictures, "jaminator-wallpaper.png");
            }
        }

        public async Task EnsureAsync(WallpaperEntry entry, bool forceReset)
        {
            if (entry == null) { _log.Info("Wallpaper: no entry in manifest, skipping"); return; }

            await _downloader.DownloadVerifiedAsync(entry.Url, LocalPath, entry.Sha256);

            var current = ReadCurrentWallpaperPath();
            var alreadySet = string.Equals(current, LocalPath, StringComparison.OrdinalIgnoreCase);

            if (alreadySet && !forceReset)
            {
                _log.Info("Wallpaper already set to canonical path");
                return;
            }

            Apply(LocalPath);
        }

        public bool IsCurrentlyCanonical()
        {
            return string.Equals(ReadCurrentWallpaperPath(), LocalPath, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadCurrentWallpaperPath()
            => (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);

        public void Apply(string path)
        {
            // Always write the registry value first — this persists across sessions even
            // if the live broadcast below silently no-ops in a non-interactive context.
            try
            {
                using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop", writable: true);
                if (k != null)
                {
                    k.SetValue("Wallpaper", path);
                    k.SetValue("WallpaperStyle", "10"); // Fill
                    k.SetValue("TileWallpaper", "0");
                }
                _log.Info("Wallpaper registry value updated");
            }
            catch (Exception ex)
            {
                _log.Warn("Could not write wallpaper registry value: " + ex.Message);
            }

            // Then ask the running session to apply it. Best-effort: silently skip on
            // non-interactive sessions (no error, the registry write will take effect
            // on next login anyway).
            var rc = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path,
                                          SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
            if (rc != 0) _log.Info($"Wallpaper applied to live session: {path}");
            else _log.Info("Wallpaper queued (will activate on next login — no live desktop in this session)");
        }
    }
}
