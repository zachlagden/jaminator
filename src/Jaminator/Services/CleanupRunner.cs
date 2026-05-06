using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Jaminator.Models;

namespace Jaminator.Services
{
    public sealed class CleanupRunner
    {
        [DllImport("Shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);
        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI   = 0x00000002;
        private const uint SHERB_NOSOUND        = 0x00000004;

        private readonly Logger _log;
        private readonly WallpaperSetter _wallpaper;

        public CleanupRunner(Logger log, WallpaperSetter wallpaper)
        {
            _log = log;
            _wallpaper = wallpaper;
        }

        public async Task RunAsync(CleanupEntry cfg, WallpaperEntry? wallpaperCfg)
        {
            await Task.Run(() =>
            {
                long freed = 0;
                foreach (var raw in cfg.TempPaths)
                {
                    var path = Environment.ExpandEnvironmentVariables(raw);
                    freed += WipeContents(path);
                }
                _log.Info($"Temp wipe: ~{freed / 1024 / 1024} MB freed");

                if (cfg.EmptyRecycleBin)
                {
                    var rc = SHEmptyRecycleBin(IntPtr.Zero, null,
                                               SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                    // 0 = success, 0x8000FFFF (-2147418113) = E_UNEXPECTED, returned when
                    // the recycle bin is already empty or the call is from a non-interactive
                    // session — neither is an error worth flagging.
                    const uint AlreadyEmpty = 0x8000FFFF;
                    if (rc == 0) _log.Info("Recycle bin emptied");
                    else if (rc == AlreadyEmpty) _log.Info("Recycle bin: already empty / no live session");
                    else _log.Warn($"Recycle bin: rc=0x{rc:X8}");
                }

                if (cfg.ClearBrowserCache != null) WipeBrowserCaches(cfg.ClearBrowserCache);

                if (cfg.DocumentsAllowlist != null && cfg.DocumentsAllowlist.Enabled)
                    QuarantineDocuments(cfg.DocumentsAllowlist);
            });

            if (cfg.ResetWallpaperIfChanged && wallpaperCfg != null)
            {
                if (!_wallpaper.IsCurrentlyCanonical())
                {
                    _log.Info("Wallpaper drift detected — resetting to canonical");
                    await _wallpaper.EnsureAsync(wallpaperCfg, forceReset: true);
                }
                else
                {
                    _log.Info("Wallpaper still canonical, no reset needed");
                }
            }
        }

        // ---------- Temp wipe ----------

        private long WipeContents(string dir)
        {
            if (!Directory.Exists(dir)) { _log.Info($"  skip (missing): {dir}"); return 0; }
            long freed = 0;
            // Manual recursive walk so per-directory access-denied (e.g. INetCache\Content.IE5
            // which has special ACLs) doesn't abort the whole wipe.
            WalkAndDelete(dir, isRoot: true, ref freed);
            _log.Info($"  cleaned: {dir}");
            return freed;
        }

        private void WalkAndDelete(string dir, bool isRoot, ref long freed)
        {
            string[] files, subdirs;
            try { files = Directory.GetFiles(dir); }
            catch (UnauthorizedAccessException) { return; }
            catch (DirectoryNotFoundException) { return; }
            catch { return; }

            foreach (var f in files)
            {
                try
                {
                    var size = new FileInfo(f).Length;
                    File.Delete(f);
                    freed += size;
                }
                catch { /* locked / denied — skip */ }
            }

            try { subdirs = Directory.GetDirectories(dir); }
            catch { return; }

            foreach (var d in subdirs) WalkAndDelete(d, isRoot: false, ref freed);

            // Don't delete the root path itself (it's a known temp dir we want to keep).
            if (!isRoot)
            {
                try { Directory.Delete(dir); } catch { }
            }
        }

        // ---------- Browser cache ----------

        private void WipeBrowserCaches(BrowserCacheEntry cfg)
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

            if (cfg.Edge)   WipeChromiumCache(Path.Combine(local, "Microsoft", "Edge", "User Data"), "Edge");
            if (cfg.Chrome) WipeChromiumCache(Path.Combine(local, "Google", "Chrome", "User Data"), "Chrome");
            if (cfg.Firefox) WipeFirefoxCache(Path.Combine(roaming, "Mozilla", "Firefox", "Profiles"));
        }

        private void WipeChromiumCache(string userDataDir, string label)
        {
            if (!Directory.Exists(userDataDir)) { _log.Info($"  {label}: not installed"); return; }
            // Each profile (Default, Profile 1, ...) has its own Cache + Code Cache
            foreach (var profile in Directory.EnumerateDirectories(userDataDir))
            {
                var cache = Path.Combine(profile, "Cache");
                var codeCache = Path.Combine(profile, "Code Cache");
                if (Directory.Exists(cache)) WipeContents(cache);
                if (Directory.Exists(codeCache)) WipeContents(codeCache);
            }
            _log.Info($"  {label} cache cleared");
        }

        private void WipeFirefoxCache(string profilesDir)
        {
            if (!Directory.Exists(profilesDir)) { _log.Info("  Firefox: not installed"); return; }
            foreach (var profile in Directory.EnumerateDirectories(profilesDir))
            {
                var cache = Path.Combine(profile, "cache2");
                if (Directory.Exists(cache)) WipeContents(cache);
            }
            _log.Info("  Firefox cache cleared");
        }

        // ---------- Documents allowlist ----------

        private void QuarantineDocuments(DocumentsAllowlistEntry cfg)
        {
            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (!Directory.Exists(docs)) return;

            // Compute the allowlist set, case-insensitive.
            var allowedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in cfg.AllowedSubfolders) allowedNames.Add(s);
            foreach (var f in cfg.AllowedFiles) allowedNames.Add(f);

            // Default-allow Windows-managed virtual folders
            allowedNames.Add("My Music");
            allowedNames.Add("My Pictures");
            allowedNames.Add("My Videos");
            allowedNames.Add("desktop.ini");
            allowedNames.Add(Path.GetFileName(cfg.QuarantineFolder)); // don't quarantine the quarantine folder

            var quarantineAbs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                cfg.QuarantineFolder.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(quarantineAbs);

            var moved = 0;
            foreach (var entry in Directory.EnumerateFileSystemEntries(docs))
            {
                var name = Path.GetFileName(entry);
                if (allowedNames.Contains(name)) continue;

                var dest = Path.Combine(quarantineAbs, name);
                dest = NextAvailable(dest);
                try
                {
                    if (Directory.Exists(entry)) Directory.Move(entry, dest);
                    else File.Move(entry, dest);
                    moved++;
                    _log.Info($"  quarantined: {name}");
                }
                catch (Exception ex)
                {
                    _log.Warn($"  could not quarantine {name}: {ex.Message}");
                }
            }
            _log.Info($"Documents allowlist: {moved} item(s) moved to {quarantineAbs}");
        }

        private static string NextAvailable(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (var i = 2; i < 1000; i++)
            {
                var candidate = Path.Combine(dir, $"{name} ({i}){ext}");
                if (!File.Exists(candidate) && !Directory.Exists(candidate)) return candidate;
            }
            return path + "." + Guid.NewGuid().ToString("N").Substring(0, 8);
        }

        // ---------- helpers ----------

        private static IEnumerable<string> EnumerateFilesSafe(string dir)
        {
            try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories); }
            catch { return Array.Empty<string>(); }
        }
        private static IEnumerable<string> EnumerateDirsSafe(string dir)
        {
            try { return Directory.EnumerateDirectories(dir); }
            catch { return Array.Empty<string>(); }
        }
    }
}
