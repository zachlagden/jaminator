using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Jaminator.Services;

namespace Jaminator
{
    internal static class Program
    {
        public const string ToolVersion = "0.7.5";
        public const string ManifestUrl =
            "https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json";

        public enum RunMode
        {
            Ui,
            RunAll,
            LoginMode,
            Install,
            Uninstall,
            RegisterTask,
            UnregisterTask
        }

        public static RunMode Mode { get; private set; }

        [STAThread]
        private static int Main(string[] args)
        {
            // Fail-fast: an EXE built by `dotnet build` directly (bypassing
            // installer/build.ps1) embeds an empty or placeholder PAT. Catch it
            // here BEFORE any mode parsing, UI init, or network fetch so a
            // silent login-mode invocation leaves a diagnostic breadcrumb in
            // %TEMP% instead of 401-ing against GitHub on first launch.
            if (string.IsNullOrEmpty(BuildSecrets.WifiPat)
                || BuildSecrets.WifiPat == "@@PAT@@")
            {
                var msg = "Jaminator build is missing the Wi-Fi PAT. Run installer/build.ps1 on a Windows box with installer/secrets/wifi-pat.txt present (or $env:JAMINATOR_WIFI_PAT set) before launching this EXE. See installer/secrets/README.md for setup.";
                Console.Error.WriteLine(msg);
                try
                {
                    var tempLog = Path.Combine(
                        Path.GetTempPath(),
                        $"Jaminator-fail-fast-{DateTime.Now:yyyyMMddHHmmss}.log");
                    File.WriteAllText(tempLog, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}{Environment.NewLine}");
                }
                catch { /* best-effort breadcrumb — read-only %TEMP% must not crash the guard itself */ }
                return 1;
            }

            Mode = ParseMode(args);

            // Headless ops: run, return exit code, never show UI.
            if (Mode == RunMode.Install || Mode == RunMode.Uninstall ||
                Mode == RunMode.RegisterTask || Mode == RunMode.UnregisterTask)
            {
                var log = new Logger();
                log.OnMessage += line => Console.WriteLine(line);
                return Mode switch
                {
                    RunMode.Install => Installer.Install(log),
                    RunMode.Uninstall => Installer.Uninstall(log),
                    RunMode.RegisterTask => Installer.RegisterScheduledTask(log),
                    RunMode.UnregisterTask => Installer.UnregisterScheduledTask(log),
                    _ => 1
                };
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
            return 0;
        }

        private static RunMode ParseMode(string[] args)
        {
            bool Has(string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
            if (Has("--install")) return RunMode.Install;
            if (Has("--uninstall")) return RunMode.Uninstall;
            if (Has("--register-task")) return RunMode.RegisterTask;
            if (Has("--unregister-task")) return RunMode.UnregisterTask;
            if (Has("--login-mode")) return RunMode.LoginMode;
            if (Has("--run-all")) return RunMode.RunAll;
            return RunMode.Ui;
        }

        public static bool RunAllOnStart => Mode == RunMode.RunAll || Mode == RunMode.LoginMode;
        public static bool ExitAfterRun  => Mode == RunMode.RunAll || Mode == RunMode.LoginMode;
        public static bool LoginModeOnly => Mode == RunMode.LoginMode;
    }
}
