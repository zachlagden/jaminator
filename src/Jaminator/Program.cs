using System;
using System.Windows.Forms;

namespace Jaminator
{
    internal static class Program
    {
        public const string ToolVersion = "0.1.0";
        public const string ManifestUrl =
            "https://raw.githubusercontent.com/zachlagden/jaminator/main/manifest/manifest.json";

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UI.MainForm());
        }
    }
}
