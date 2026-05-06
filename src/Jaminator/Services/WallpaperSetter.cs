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

            // Download (or refresh) the canonical wallpaper file
            await _downloader.DownloadVerifiedAsync(entry.Url, LocalPath, entry.Sha256);

            var current = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
            var alreadySet = string.Equals(current, LocalPath, StringComparison.OrdinalIgnoreCase);

            if (alreadySet && !forceReset)
            {
                _log.Info("Wallpaper already set to canonical path");
                return;
            }

            Apply(LocalPath);
            _log.Info($"Wallpaper set: {LocalPath}");
        }

        public bool IsCurrentlyCanonical()
        {
            var current = (string?)Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
            return string.Equals(current, LocalPath, StringComparison.OrdinalIgnoreCase);
        }

        public void Apply(string path)
        {
            var rc = SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path,
                                          SPIF_UPDATEINIFILE | SPIF_SENDWININICHANGE);
            if (rc == 0) throw new InvalidOperationException("SystemParametersInfo failed to set wallpaper");
        }
    }
}
