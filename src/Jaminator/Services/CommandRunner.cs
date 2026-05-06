using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Jaminator.Models;

namespace Jaminator.Services
{
    public sealed class CommandRunner
    {
        private readonly Logger _log;
        public CommandRunner(Logger log) { _log = log; }

        public async Task RunAsync(IEnumerable<CommandEntry> commands)
        {
            foreach (var cmd in commands) await RunOneAsync(cmd);
        }

        public async Task RunOneAsync(CommandEntry cmd)
        {
            _log.Info($"Command: {cmd.Name}");

            if (!string.IsNullOrWhiteSpace(cmd.SkipIf))
            {
                if (await EvaluateSkipIfAsync(cmd.SkipIf!))
                {
                    _log.Info("  already in target state, skipping");
                    return;
                }
            }

            ProcessStartInfo psi;
            switch (cmd.Shell?.ToLowerInvariant())
            {
                case "cmd":
                    psi = new ProcessStartInfo("cmd.exe", "/c " + cmd.Script);
                    break;
                case "powershell":
                case null:
                case "":
                    psi = new ProcessStartInfo("powershell.exe",
                        "-NoProfile -ExecutionPolicy Bypass -Command \"" + EscapePs(cmd.Script) + "\"");
                    break;
                default:
                    _log.Warn($"  unknown shell '{cmd.Shell}', skipping");
                    return;
            }

            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using var p = new Process { StartInfo = psi };
            p.OutputDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _log.Info("  | " + e.Data); };
            p.ErrorDataReceived  += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _log.Warn("  | " + e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            await Task.Run(() => p.WaitForExit());

            if (p.ExitCode == 0) _log.Info($"  -> ok");
            else _log.Warn($"  -> exit {p.ExitCode}");
        }

        // PowerShell -Command receives a string; escape embedded double quotes.
        private static string EscapePs(string script) => script.Replace("\"", "\\\"");

        /// <summary>
        /// Runs the SkipIf expression as PowerShell. Exit 0 = condition true (skip).
        /// Anything else (including parse errors) = run the command anyway, fail-open.
        /// </summary>
        private async Task<bool> EvaluateSkipIfAsync(string expression)
        {
            var wrapped = $"if ({expression}) {{ exit 0 }} else {{ exit 1 }}";
            var psi = new ProcessStartInfo("powershell.exe",
                "-NoProfile -ExecutionPolicy Bypass -Command \"" + EscapePs(wrapped) + "\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                using var p = Process.Start(psi)!;
                await Task.Run(() => p.WaitForExit(15000));
                return p.HasExited && p.ExitCode == 0;
            }
            catch { return false; }
        }
    }
}
