using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.Win32;

namespace Jaminator.Services
{
    /// <summary>
    /// Handles install / uninstall plumbing. Two entry surfaces:
    ///   • <see cref="Install"/> / <see cref="Uninstall"/> — full self-installer
    ///     used by the standalone `--install` flag.
    ///   • <see cref="RegisterScheduledTask"/> / <see cref="UnregisterScheduledTask"/>
    ///     — lightweight task-only ops called by the WiX MSI's custom actions
    ///     (since the MSI handles the file copy + Start Menu shortcut itself).
    /// </summary>
    public static class Installer
    {
        public const string TaskName = "Jaminator-Login";
        public const string DailyTaskName = "Jaminator-Daily";

        public static string InstallDir =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Jaminator");

        public static string InstalledExe => Path.Combine(InstallDir, "Jaminator.exe");

        public static bool IsInstalled => File.Exists(InstalledExe);

        public static bool IsRunningFromInstallDir =>
            string.Equals(
                Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName),
                InstallDir,
                StringComparison.OrdinalIgnoreCase);

        // ---------- Full self-install (manual / scriptless deploy) ----------

        public static int Install(Logger log)
        {
            try
            {
                Directory.CreateDirectory(InstallDir);
                var current = Process.GetCurrentProcess().MainModule!.FileName;
                var currentDir = Path.GetDirectoryName(current)!;

                foreach (var f in Directory.EnumerateFiles(currentDir))
                {
                    var dst = Path.Combine(InstallDir, Path.GetFileName(f));
                    if (string.Equals(f, current, StringComparison.OrdinalIgnoreCase) &&
                        IsRunningFromInstallDir) continue;
                    CopyWithRetry(f, dst, log);
                }
                log.Info($"Installed to {InstallDir}");

                RegisterScheduledTask(log);
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

        /// <summary>
        /// Walks the Add/Remove Programs registry and returns the MSI ProductCode
        /// of the installed Jaminator (if any). Lets the UI hand off to the real
        /// Windows Installer for uninstall instead of doing its own filesystem ops.
        /// </summary>
        public static string? FindMsiProductCode(string displayName = "Jaminator")
        {
            var roots = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };
            foreach (var root in roots)
            {
                using var key = Registry.LocalMachine.OpenSubKey(root);
                if (key == null) continue;
                foreach (var subName in key.GetSubKeyNames())
                {
                    using var sub = key.OpenSubKey(subName);
                    if (sub == null) continue;
                    var name = sub.GetValue("DisplayName") as string;
                    if (string.Equals(name, displayName, StringComparison.OrdinalIgnoreCase) &&
                        subName.StartsWith("{") && subName.EndsWith("}"))
                    {
                        return subName;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Launches msiexec /x against the installed Jaminator MSI. The current
        /// process should exit immediately afterwards so msiexec can replace and
        /// delete files (we are running from inside the install directory).
        /// </summary>
        public static bool LaunchMsiUninstall(Logger log)
        {
            var productCode = FindMsiProductCode();
            if (productCode == null) return false;

            var psi = new ProcessStartInfo("msiexec.exe", $"/x {productCode} /qb")
            {
                UseShellExecute = true
            };
            Process.Start(psi);
            log.Info($"Handed off to msiexec /x {productCode} — exiting Jaminator now");
            return true;
        }

        public static int Uninstall(Logger log)
        {
            try
            {
                UnregisterScheduledTask(log);
                RemoveStartMenuShortcut(log);

                if (Directory.Exists(InstallDir))
                {
                    if (IsRunningFromInstallDir)
                        ScheduleDelayedDirDelete(InstallDir, log);
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

        // ---------- Task-only ops (used by MSI custom actions) ----------

        public static int RegisterScheduledTask(Logger log)
        {
            string? xmlPath = null;
            try
            {
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

                xmlPath = Path.Combine(Path.GetTempPath(), $"jaminator-task-{Guid.NewGuid():N}.xml");
                File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

                try
                {
                    RunSchTasks($"/Create /TN \"{TaskName}\" /XML \"{xmlPath}\" /F");
                    log.Info("Scheduled task registered: " + TaskName);
                    // Delete the task XML on success only (NOT in finally — failure path preserves it).
                    try { File.Delete(xmlPath); } catch { }
                    xmlPath = null;
                    return 0;
                }
                catch
                {
                    // Preserve the XML for diagnostics; don't delete it. Re-throw to outer catch.
                    throw;
                }
            }
            catch (Exception ex)
            {
                log.Error("Failed to register scheduled task", ex);
                WriteRegisterTaskDiagnosticLog(ex, xmlPath);
                return 1;
            }
        }

        private static void WriteRegisterTaskDiagnosticLog(Exception ex, string? preservedXmlPath)
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                var path = Path.Combine(Path.GetTempPath(),
                    $"Jaminator-register-task-error-{timestamp}.log");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Jaminator --register-task diagnostic log");
                sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-ddTHH:mm:ss} local");
                sb.AppendLine("Run mode: --register-task");
                sb.AppendLine($"Tool version: {Jaminator.Program.ToolVersion}");
                sb.AppendLine();
                sb.AppendLine("--- Exception ---");
                sb.AppendLine($"Type: {ex.GetType().FullName}");
                sb.AppendLine($"Message: {ex.Message}");
                sb.AppendLine("Stack trace:");
                sb.AppendLine(ex.StackTrace ?? "(no stack)");
                sb.AppendLine();

                if (ex is SchTasksException sch)
                {
                    sb.AppendLine("--- Captured schtasks.exe output ---");
                    sb.AppendLine($"Command line: {sch.CommandLine}");
                    sb.AppendLine($"Exit code: {sch.ExitCode}");
                    sb.AppendLine("STDOUT:");
                    sb.AppendLine(sch.Stdout);
                    sb.AppendLine("STDERR:");
                    sb.AppendLine(sch.Stderr);
                    sb.AppendLine();
                }

                if (preservedXmlPath != null && File.Exists(preservedXmlPath))
                {
                    sb.AppendLine("--- Failing task XML ---");
                    sb.AppendLine($"Preserved at: {preservedXmlPath}");
                    sb.AppendLine("(deleted on success; preserved on failure for diagnostics)");
                }

                File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
                Console.WriteLine($"Diagnostic log written: {path}");  // surfaces in MSI verbose log
            }
            catch
            {
                // Never let diagnostic-log writing fail the diagnostics path itself.
            }
        }

        public static int UnregisterScheduledTask(Logger log)
        {
            try
            {
                RunSchTasks($"/Delete /TN \"{TaskName}\" /F", allowFailure: true);
                RunSchTasks($"/Delete /TN \"{DailyTaskName}\" /F", allowFailure: true);
                log.Info("Scheduled tasks removed");
                return 0;
            }
            catch (Exception ex)
            {
                log.Warn("Could not remove scheduled task: " + ex.Message);
                return 0;
            }
        }

        // ---------- Daily Run All task (reconciled from manifest at every logon) ----------

        /// <summary>
        /// Ensures a daily "run everything" scheduled task exists at <paramref name="hhmm"/>.
        /// Pass null to remove the task. Idempotent — re-calling with the same time is a no-op.
        /// </summary>
        public static void ReconcileDailyTask(string? hhmm, Logger log)
        {
            if (string.IsNullOrWhiteSpace(hhmm))
            {
                if (DailyTaskExists())
                {
                    RunSchTasks($"/Delete /TN \"{DailyTaskName}\" /F", allowFailure: true);
                    log.Info("Daily auto-run task removed (manifest disabled it)");
                }
                return;
            }

            // Validate format strictly so we never write a malformed task XML.
            if (!System.Text.RegularExpressions.Regex.IsMatch(hhmm!, @"^\d{2}:\d{2}$"))
            {
                log.Warn($"Invalid schedule.dailyRunAll value '{hhmm}' (expected HH:MM); skipping.");
                return;
            }

            if (DailyTaskRunsAt(hhmm!))
            {
                log.Info($"Daily auto-run already scheduled at {hhmm}");
                return;
            }

            var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.4"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo>
    <Description>Jaminator daily 'Run All' (full cleanup, app installs, admin commands)</Description>
    <Author>Jam Coding</Author>
  </RegistrationInfo>
  <Triggers>
    <CalendarTrigger>
      <StartBoundary>2026-01-01T{hhmm}:00</StartBoundary>
      <Enabled>true</Enabled>
      <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
    </CalendarTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>S-1-5-18</UserId>
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
      <StopOnIdleEnd>false</StopOnIdleEnd>
      <RestartOnIdle>false</RestartOnIdle>
    </IdleSettings>
    <AllowStartOnDemand>true</AllowStartOnDemand>
    <Enabled>true</Enabled>
    <Hidden>false</Hidden>
    <RunOnlyIfIdle>false</RunOnlyIfIdle>
    <WakeToRun>false</WakeToRun>
    <ExecutionTimeLimit>PT1H</ExecutionTimeLimit>
    <Priority>7</Priority>
  </Settings>
  <Actions Context=""Author"">
    <Exec>
      <Command>{InstalledExe}</Command>
      <Arguments>--run-all</Arguments>
      <WorkingDirectory>{InstallDir}</WorkingDirectory>
    </Exec>
  </Actions>
</Task>";

            var xmlPath = Path.Combine(Path.GetTempPath(), $"jaminator-daily-{Guid.NewGuid():N}.xml");
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
            try
            {
                RunSchTasks($"/Create /TN \"{DailyTaskName}\" /XML \"{xmlPath}\" /F");
                log.Info($"Daily auto-run scheduled for {hhmm}");
            }
            catch (Exception ex) { log.Warn("Could not register daily task: " + ex.Message); }
            finally { try { File.Delete(xmlPath); } catch { } }
        }

        private static bool DailyTaskExists()
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    $"/Query /TN \"{DailyTaskName}\"")
                { UseShellExecute = false, CreateNoWindow = true,
                  RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit();
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        private static bool DailyTaskRunsAt(string hhmm)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo("schtasks.exe",
                    $"/Query /TN \"{DailyTaskName}\" /V /FO LIST")
                { UseShellExecute = false, CreateNoWindow = true,
                  RedirectStandardOutput = true, RedirectStandardError = true };
                using var p = System.Diagnostics.Process.Start(psi)!;
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (p.ExitCode != 0) return false;
                return output.Contains($"{hhmm}:00");
            }
            catch { return false; }
        }

        // ---------- Start Menu shortcut (only used by self-installer; MSI does its own) ----------

        private static string StartMenuLnk =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                "Programs", "Jaminator.lnk");

        private static void CreateStartMenuShortcut(Logger log)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StartMenuLnk)!);
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

        private static void RunSchTasks(string args, bool allowFailure = false)
        {
            var psi = new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = Process.Start(psi)!;

            // Drain stderr asynchronously so we can drain stdout synchronously
            // without risking a pipe-buffer deadlock. See:
            // learn.microsoft.com/dotnet/api/system.diagnostics.processstartinfo.redirectstandardoutput
            var stderrBuf = new System.Text.StringBuilder();
            p.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null) stderrBuf.AppendLine(e.Data);
            };
            p.BeginErrorReadLine();

            string stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            string stderr = stderrBuf.ToString();

            if (p.ExitCode != 0 && !allowFailure)
            {
                throw new SchTasksException(
                    commandLine: $"schtasks.exe {args}",
                    exitCode: p.ExitCode,
                    stdout: stdout,
                    stderr: stderr);
            }
        }

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

    internal sealed class SchTasksException : Exception
    {
        public string CommandLine { get; }
        public int ExitCode { get; }
        public string Stdout { get; }
        public string Stderr { get; }

        public SchTasksException(string commandLine, int exitCode, string stdout, string stderr)
            : base($"{commandLine} exit {exitCode}: {(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim()}")
        {
            CommandLine = commandLine;
            ExitCode = exitCode;
            Stdout = stdout ?? "";
            Stderr = stderr ?? "";
        }
    }
}
