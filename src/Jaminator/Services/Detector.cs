using System;
using System.Diagnostics;
using Jaminator.Models;
using Microsoft.Win32;

namespace Jaminator.Services
{
    /// <summary>
    /// Decides whether a program in the manifest is already installed and at a
    /// good-enough version, so the installer can skip it.
    /// </summary>
    public static class Detector
    {
        public static bool IsInstalled(DetectEntry? d)
        {
            if (d == null) return false;

            if (!string.IsNullOrWhiteSpace(d.FilePath))
            {
                var path = Environment.ExpandEnvironmentVariables(d.FilePath!);
                if (System.IO.File.Exists(path)) return true;
            }

            if (!string.IsNullOrWhiteSpace(d.RegistryKey))
            {
                var (hive, sub) = SplitKey(d.RegistryKey!);
                using var k = hive?.OpenSubKey(sub);
                if (k == null) return false;

                if (string.IsNullOrWhiteSpace(d.MinVersion)) return true;

                var ver = k.GetValue("DisplayVersion") as string
                          ?? k.GetValue("Version") as string;
                if (string.IsNullOrEmpty(ver)) return true;
                return CompareVersions(ver!, d.MinVersion!) >= 0;
            }

            if (!string.IsNullOrWhiteSpace(d.AppxPackageName))
            {
                return AppxPresent(d.AppxPackageName!);
            }

            return false;
        }

        private static (RegistryKey? hive, string subKey) SplitKey(string full)
        {
            var idx = full.IndexOf('\\');
            if (idx < 0) return (null, "");
            var root = full.Substring(0, idx).ToUpperInvariant();
            var sub = full.Substring(idx + 1);
            RegistryKey? hive = root switch
            {
                "HKLM" or "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
                "HKCU" or "HKEY_CURRENT_USER" => Registry.CurrentUser,
                _ => null
            };
            return (hive, sub);
        }

        private static int CompareVersions(string a, string b)
        {
            try
            {
                if (Version.TryParse(NormaliseVersion(a), out var va) &&
                    Version.TryParse(NormaliseVersion(b), out var vb))
                    return va.CompareTo(vb);
            }
            catch { }
            return string.CompareOrdinal(a, b);
        }

        private static string NormaliseVersion(string v)
        {
            // Pad to 4 parts so Version.TryParse doesn't choke on "3.29".
            var parts = v.Split('.');
            return parts.Length switch
            {
                1 => $"{v}.0.0.0",
                2 => $"{v}.0.0",
                3 => $"{v}.0",
                _ => v
            };
        }

        private static bool AppxPresent(string packageName)
        {
            // Use PowerShell to query — avoids needing the System.Runtime.WindowsRuntime mess.
            var psi = new ProcessStartInfo("powershell.exe",
                $"-NoProfile -Command \"if (Get-AppxPackage -Name '{packageName}' -EA 0) {{ exit 0 }} else {{ exit 1 }}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using var p = Process.Start(psi)!;
                p.WaitForExit(15000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
