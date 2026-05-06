using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;
using Jaminator.Models;

namespace Jaminator.Services
{
    /// <summary>
    /// Installs programs from manifest entries. Handles MSI, EXE, and zip-extract
    /// (portable apps deployed by xcopy + shortcut creation). Honours per-arch
    /// prerequisites — they install (in order) before the main installer.
    /// </summary>
    public sealed class MsiInstaller
    {
        private readonly Logger _log;
        private readonly Downloader _downloader;

        public MsiInstaller(Logger log, Downloader downloader)
        {
            _log = log;
            _downloader = downloader;
        }

        public async Task InstallAllAsync(IEnumerable<ProgramEntry> programs)
        {
            foreach (var p in programs)
            {
                try { await InstallOneAsync(p); }
                catch (Exception ex)
                {
                    // Don't let one program's failure halt the rest of the fleet install.
                    _log.Error($"Program '{p.Name}' failed", ex);
                }
            }
        }

        public async Task InstallOneAsync(ProgramEntry p)
        {
            _log.Info($"Program: {p.Name}");

            if (Detector.IsInstalled(p.Detect))
            {
                _log.Info("  already installed, skipping");
                return;
            }

            var arch = Environment.Is64BitOperatingSystem ? p.X64 : p.X86;
            arch ??= p.X64 ?? p.X86;
            if (arch == null)
            {
                _log.Warn("  no installer configured for this architecture, skipping");
                return;
            }

            // Prerequisites first (in order).
            for (var i = 0; i < arch.Prerequisites.Count; i++)
            {
                _log.Info($"  prerequisite {i + 1}/{arch.Prerequisites.Count}");
                await RunInstallerAsync(p.Id, arch.Prerequisites[i], isPrereq: true);
            }

            await RunInstallerAsync(p.Id, arch, isPrereq: false);

            if (Detector.IsInstalled(p.Detect)) _log.Info("  install verified");
            else _log.Warn("  install completed but detect rule did not match");
        }

        private async Task RunInstallerAsync(string programId, ArchEntry arch, bool isPrereq)
        {
            if (string.IsNullOrWhiteSpace(arch.Url) ||
                arch.Url.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn($"  {(isPrereq ? "prerequisite" : "installer")} URL is a placeholder, skipping");
                return;
            }

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Jaminator", "cache");
            Directory.CreateDirectory(cacheDir);

            var ext = arch.Kind?.ToLowerInvariant() switch
            {
                "msi" => ".msi",
                "exe" => ".exe",
                "zip-extract" => ".zip",
                _ => Path.GetExtension(arch.Url)
            };
            var archTag = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            var fileName = $"{programId}-{(isPrereq ? "prereq-" : "")}{archTag}{ext}";
            var localPath = Path.Combine(cacheDir, fileName);

            await _downloader.DownloadVerifiedAsync(arch.Url, localPath, arch.Sha256);

            switch ((arch.Kind ?? "msi").ToLowerInvariant())
            {
                case "msi":
                    await RunMsiAsync(localPath, arch.Args ?? "/qn /norestart");
                    break;
                case "exe":
                    await RunExeAsync(localPath, arch.Args ?? "/S");
                    break;
                case "zip-extract":
                    ExtractAndShortcut(localPath, arch);
                    break;
                default:
                    _log.Warn($"  unknown installer kind '{arch.Kind}', skipping");
                    break;
            }
        }

        // ---------- MSI ----------

        private async Task RunMsiAsync(string msiPath, string args)
        {
            var fullArgs = $"/i \"{msiPath}\" {args} /L*V \"{msiPath}.log\"";
            _log.Info($"  msiexec {fullArgs}");

            var psi = new ProcessStartInfo("msiexec.exe", fullArgs)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("msiexec failed to start");
            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode == 0) _log.Info("  -> ok");
            else if (p.ExitCode == 3010) _log.Warn("  -> ok (reboot required)");
            else throw new Exception($"msiexec exit {p.ExitCode}. Log: {msiPath}.log");
        }

        // ---------- EXE ----------

        private async Task RunExeAsync(string exePath, string args)
        {
            _log.Info($"  {Path.GetFileName(exePath)} {args}");
            var psi = new ProcessStartInfo(exePath, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("installer failed to start");
            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode == 0) _log.Info("  -> ok");
            else if (p.ExitCode == 3010) _log.Warn("  -> ok (reboot required)");
            else throw new Exception($"installer exit {p.ExitCode}");
        }

        // ---------- zip-extract ----------

        private void ExtractAndShortcut(string zipPath, ArchEntry arch)
        {
            if (string.IsNullOrWhiteSpace(arch.InstallPath))
                throw new InvalidOperationException("zip-extract requires installPath");

            var dest = Environment.ExpandEnvironmentVariables(arch.InstallPath);
            // .NET Framework 4.8 ExtractToDirectory has no overwrite flag, so unpack
            // into a temp dir then xcopy on top — survives reinstalls cleanly.
            var staging = dest + ".staging";
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            Directory.CreateDirectory(staging);
            _log.Info($"  extracting to {dest}");
            ZipFile.ExtractToDirectory(zipPath, staging);
            CopyOverlayDir(staging, dest);
            Directory.Delete(staging, recursive: true);

            if (string.IsNullOrWhiteSpace(arch.ExeName)) return;

            var exePath = Path.Combine(dest, arch.ExeName!);
            if (!File.Exists(exePath))
            {
                _log.Warn($"  exeName '{arch.ExeName}' not found at {exePath}");
                return;
            }

            var label = arch.ShortcutName
                        ?? Path.GetFileNameWithoutExtension(arch.ExeName!);

            if (arch.DesktopShortcut)
            {
                var target = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    label + ".lnk");
                CreateShortcut(target, exePath, dest);
                _log.Info($"  desktop shortcut: {target}");
            }
            if (arch.StartMenuShortcut)
            {
                var smDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu);
                var target = Path.Combine(smDir, "Programs", label + ".lnk");
                CreateShortcut(target, exePath, dest);
                _log.Info($"  start-menu shortcut: {target}");
            }
        }

        private static void CopyOverlayDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var dir in Directory.EnumerateDirectories(src, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dir.Replace(src, dst));
            }
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                File.Copy(file, file.Replace(src, dst), overwrite: true);
            }
        }

        private static void CreateShortcut(string lnkPath, string targetExe, string workingDir)
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell")
                            ?? throw new InvalidOperationException("WScript.Shell unavailable");
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            shortcut.TargetPath = targetExe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Save();
        }
    }
}
