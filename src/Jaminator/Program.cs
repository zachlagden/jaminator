using System;
using System.Linq;
using System.Windows.Forms;
using Jaminator.Services;

namespace Jaminator
{
    internal static class Program
    {
        public const string ToolVersion = "0.4.0";
        public const string ManifestUrl =
            "https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json";

        public enum RunMode { Ui, RunAll, LoginMode, Install, Uninstall }

        public static RunMode Mode { get; private set; }

        [STAThread]
        private static int Main(string[] args)
        {
            Mode = ParseMode(args);

            // Install / uninstall are non-UI: do work, return exit code, exit.
            if (Mode == RunMode.Install || Mode == RunMode.Uninstall)
            {
                var log = new Logger();
                log.OnMessage += line => Console.WriteLine(line);
                return Mode == RunMode.Install ? Installer.Install(log) : Installer.Uninstall(log);
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
            if (Has("--login-mode")) return RunMode.LoginMode;
            if (Has("--run-all")) return RunMode.RunAll;
            return RunMode.Ui;
        }

        // Convenience flags consumed elsewhere
        public static bool RunAllOnStart => Mode == RunMode.RunAll || Mode == RunMode.LoginMode;
        public static bool ExitAfterRun  => Mode == RunMode.RunAll || Mode == RunMode.LoginMode;
        public static bool LoginModeOnly => Mode == RunMode.LoginMode;
    }
}
