using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Jaminator.Services
{
    /// <summary>
    /// Self-installer: copies Jaminator into %ProgramFiles%\Jaminator\, registers
    /// a scheduled task that auto-runs the login-safe sections at every user logon,
    /// and drops a Start Menu shortcut so the tech can launch the full UI.
    /// </summary>
    public static class Installer
    {
        public const string TaskName = "Jaminator-Login";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jaminator");

        public static string InstalledExe => Path.Combine(InstallDir, "Jaminator.exe");

        public static bool IsInstalled => File.Exists(InstalledExe);

        public static bool IsRunningFromInstallDir =>
            string.Equals(
                Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName),
                InstallDir,
                StringComparison.OrdinalIgnoreCase);

        // ---------- Install ----------

        public static int Install(Logger log)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                var current = Process.GetCurrentProcess().MainModule!.FileName;
                var currentDir = Path.GetDirectoryName(current)!;

                // Copy EXE + every file alongside it (Newtonsoft.Json.dll, etc.)
                foreach (var f in Directory.EnumerateFiles(currentDir))
                {
                    var dst = Path.Combine(InstallDir, Path.GetFileName(f));
                    // Don't try to overwrite the running EXE if launched from install dir
                    if (string.Equals(f, current, StringComparison.OrdinalIgnoreCase) &&
                        IsRunningFromInstallDir) continue;
                    CopyWithRetry(f, dst, log);
                }
                log.Info($"Installed to {InstallDir}");

                CreateLogonScheduledTask(log);
                CreateStartMenuShortcut(log);

                log.Info("");
                log.Info("Install complete. Jaminator will auto-run login-safe actions on every user logon.");
                log.Info("Open from Start Menu to run cleanup / installs / admin commands.");
                return 0;
            }
            catch (Exception ex)
            {
                log.Error("Install failed", ex);
                return 1;
            }
        }

        public static int Uninstall(Logger log)
        {
            try
            {
                RemoveScheduledTask(log);
                RemoveStartMenuShortcut(log);

                if (Directory.Exists(InstallDir))
                {
                    // If running from the install dir, schedule a delayed delete
                    if (IsRunningFromInstallDir)
                    {
                        ScheduleDelayedDirDelete(InstallDir, log);
                    }
                    else
                    {
                        Directory.Delete(InstallDir, recursive: true);
                        log.Info($"Removed {InstallDir}");
                    }
                }

                log.Info("Uninstall complete.");
                return 0;
            }
            catch (Exception ex)
            {
                log.Error("Uninstall failed", ex);
                return 1;
            }
        }

        // ---------- Scheduled task ----------

        private static void CreateLogonScheduledTask(Logger log)
        {
            // Build XML so we can set "InteractiveToken" — runs as the logging-in user,
            // so HKCU writes (wallpaper) land in the right hive.
            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Jaminator login-safe maintenance (folders, wallpaper)</Description>
    <Author>Jam Coding</Author>
  </RegistrationInfo>
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <Delay>PT15S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <GroupId>S-1-5-32-545</GroupId>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
    <IdleSettings>
      <StopOnIdleEnd>true</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT10M</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{InstalledExe}</Command>
      <Arguments>--login-mode</Arguments>
      <WorkingDirectory>{InstallDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

            var xmlPath = Path.Combine(Path.GetTempPath(), $"jaminator-task-{Guid.NewGuid():N}.xml");
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

            try
            {
                RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F", log);
                log.Info("Scheduled task registered: " + TaskName);
            }
            finally
            {
                try { File.Delete(xmlPath); } catch { }
            }
        }

        private static void RemoveScheduledTask(Logger log)
        {
            try
            {
                RunSchTasks($"/Delete /TN \"{TaskName}\" /F", log, allowFailure: true);
                log.Info("Scheduled task removed");
            }
            catch (Exception ex) { log.Warn("Could not remove scheduled task: " + ex.Message); }
        }

        private static void RunSchTasks(string args, Logger log, bool allowFailure = false)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 && !allowFailure)
                throw new Exception($"schtasks {args} exit {p.ExitCode}: {stderr.Trim()} {stdout.Trim()}");
        }

        // ---------- Start Menu shortcut ----------

        private static string StartMenuLnk =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs", "Jaminator.lnk");

        private static void CreateStartMenuShortcut(Logger log)
        {
            try
            {
                var dir = Path.GetDirectoryName(StartMenuLnk)!;
                Directory.CreateDirectory(dir);

                var shellType = Type.GetTypeFromProgID("WScript.Shell")
                                ?? throw new InvalidOperationException("WScript.Shell unavailable");
                dynamic shell = Activator.CreateInstance(shellType)!;
                dynamic sc = shell.CreateShortcut(StartMenuLnk);
                sc.TargetPath = InstalledExe;
                sc.WorkingDirectory = InstallDir;
                sc.Description = "Jaminator — Jam Coding laptop maintenance";
                sc.Save();
                log.Info($"Start Menu shortcut: {StartMenuLnk}");
            }
            catch (Exception ex) { log.Warn("Could not create Start Menu shortcut: " + ex.Message); }
        }

        private static void RemoveStartMenuShortcut(Logger log)
        {
            try { if (File.Exists(StartMenuLnk)) File.Delete(StartMenuLnk); }
            catch (Exception ex) { log.Warn("Could not remove Start Menu shortcut: " + ex.Message); }
        }

        // ---------- helpers ----------

        private static void CopyWithRetry(string src, string dst, Logger log)
        {
            for (var i = 0; i < 5; i++)
            {
                try { File.Copy(src, dst, overwrite: true); return; }
                catch (IOException) { Thread.Sleep(200); }
            }
            File.Copy(src, dst, overwrite: true);
        }

        private static void ScheduleDelayedDirDelete(string dir, Logger log)
        {
            // PowerShell script that waits for our process to exit then nukes the dir.
            var pid = Process.GetCurrentProcess().Id;
            var script = $@"
$ErrorActionPreference = 'SilentlyContinue'
try {{ (Get-Process -Id {pid}).WaitForExit() }} catch {{ Start-Sleep -Seconds 2 }}
Start-Sleep -Seconds 1
Remove-Item -LiteralPath '{dir}' -Recurse -Force
";
            var sp = Path.Combine(Path.GetTempPath(), $"jaminator-uninstall-{Guid.NewGuid():N}.ps1");
            File.WriteAllText(sp, script);

            Process.Start(new ProcessStartInfo("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{sp}\"")
            { UseShellExecute = true });

            log.Info("Install dir delete scheduled (after exit)");
        }
    }
}
