using System;
using System.Drawing;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Jaminator.Models;
using Jaminator.Services;

namespace Jaminator.UI
{
    internal sealed partial class MainForm : Form
    {
        private readonly ManifestFetcher _fetcher = new ManifestFetcher();
        private Manifest? _manifest;

        public MainForm()
        {
            InitializeComponent();
            Load += OnLoad;
        }

        private async void OnLoad(object? sender, EventArgs e)
        {
            Log($"Jaminator v{Program.ToolVersion}");
            Log($"Architecture: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} Windows");
            Log($"Fetching manifest: {Program.ManifestUrl}");
            try
            {
                _manifest = await _fetcher.FetchAsync(Program.ManifestUrl);
                Log($"Manifest version: {_manifest.ManifestVersion}");
                RenderManifestSummary(_manifest);
                _runAllButton.Enabled = true;
            }
            catch (Exception ex)
            {
                Log("ERROR fetching manifest: " + ex.Message);
            }
        }

        private void RenderManifestSummary(Manifest m)
        {
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine($"Wallpaper: {(m.Wallpaper == null ? "(none)" : m.Wallpaper.Url)}");
            sb.AppendLine($"Folders to ensure: {m.Folders.Count}");
            foreach (var f in m.Folders) sb.AppendLine($"  - {f.Path}");
            sb.AppendLine($"Programs: {m.Programs.Count}");
            foreach (var p in m.Programs) sb.AppendLine($"  - {p.Name} ({p.Id})");
            sb.AppendLine($"Commands: {m.Commands.Count}");
            foreach (var c in m.Commands) sb.AppendLine($"  - {c.Name}");
            sb.AppendLine($"Cleanup configured: {(m.Cleanup != null ? "yes" : "no")}");
            Log(sb.ToString());
        }

        private void OnRunAllClick(object? sender, EventArgs e)
        {
            Log("");
            Log("[Run All] not yet implemented — services are stubbed in this scaffold.");
            Log("Coming next: cleanup runner, MSI installer, folder sync, wallpaper enforcement, command runner.");
        }

        private void Log(string message)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(Log), message); return; }
            _logBox.AppendText(message + Environment.NewLine);
        }
    }
}
