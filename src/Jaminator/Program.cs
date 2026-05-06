using System;
using System.Linq;
using System.Windows.Forms;

namespace Jaminator
{
    internal static class Program
    {
        public const string ToolVersion = "0.3.0";
        public const string ManifestUrl =
            "https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json";

        public static bool RunAllOnStart { get; private set; }
        public static bool ExitAfterRun { get; private set; }

        [STAThread]
        private static void Main(string[] args)
        {
            RunAllOnStart = args.Any(a => a.Equals("--run-all", StringComparison.OrdinalIgnoreCase));
            ExitAfterRun  = args.Any(a => a.Equals("--exit", StringComparison.OrdinalIgnoreCase))
                            || RunAllOnStart;

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
        }
    }
}
