using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Jaminator.Models;

namespace Jaminator.Services
{
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
            foreach (var p in programs) await InstallOneAsync(p);
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
            arch ??= p.X64 ?? p.X86; // fall back to whatever's available
            if (arch == null)
            {
                _log.Warn("  no MSI URL configured for this architecture, skipping");
                return;
            }

            if (string.IsNullOrWhiteSpace(arch.Url) ||
                arch.Url.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn("  MSI URL is a placeholder, skipping");
                return;
            }

            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Jaminator", "cache");
            var msiName = $"{p.Id}-{(Environment.Is64BitOperatingSystem ? "x64" : "x86")}.msi";
            var msiPath = Path.Combine(cacheDir, msiName);

            await _downloader.DownloadVerifiedAsync(arch.Url, msiPath, arch.Sha256);

            await RunMsiAsync(msiPath, arch.Args ?? "/quiet /norestart");

            // Re-detect to confirm
            if (Detector.IsInstalled(p.Detect)) _log.Info("  install verified");
            else _log.Warn("  install completed but detect rule did not match");
        }

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

            // 0 = success, 3010 = success but reboot required
            if (p.ExitCode == 0) _log.Info("  -> ok");
            else if (p.ExitCode == 3010) _log.Warn("  -> ok (reboot required)");
            else throw new Exception($"msiexec exit {p.ExitCode}. Log: {msiPath}.log");
        }
    }
}
