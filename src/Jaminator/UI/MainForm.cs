using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Jaminator.Models;
using Jaminator.Services;

namespace Jaminator.UI
{
    internal sealed partial class MainForm : Form
    {
        private readonly Logger _log;
        private readonly ManifestFetcher _fetcher = new ManifestFetcher();
        private readonly Downloader _downloader;
        private readonly FolderManager _folders;
        private readonly WallpaperSetter _wallpaper;
        private readonly CleanupRunner _cleanup;
        private readonly CommandRunner _commands;
        private readonly MsiInstaller _msi;
        private readonly SelfUpdater _updater;

        private Manifest? _manifest;

        public MainForm()
        {
            InitializeComponent();

            _log = new Logger();
            _log.OnMessage += UiAppendLog;

            _downloader = new Downloader(_log);
            _folders = new FolderManager(_log);
            _wallpaper = new WallpaperSetter(_log, _downloader);
            _cleanup = new CleanupRunner(_log, _wallpaper);
            _commands = new CommandRunner(_log);
            _msi = new MsiInstaller(_log, _downloader);
            _updater = new SelfUpdater(_log);

            Load += OnLoad;
            _runAllButton.Click += async (_, _) => await RunAllAsync();
        }

        private async void OnLoad(object? sender, EventArgs e)
        {
            _log.Info($"Jaminator v{Program.ToolVersion}");
            _log.Info($"Architecture: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} Windows");

            // Self-update check (non-blocking; just logs / prompts)
            _ = Task.Run(async () =>
            {
                var info = await _updater.CheckAsync(Program.ToolVersion);
                if (info != null) PromptUpdate(info);
            });

            _log.Info($"Fetching manifest: {Program.ManifestUrl}");
            try
            {
                _manifest = await _fetcher.FetchAsync(Program.ManifestUrl);
                _log.Info($"Manifest version: {_manifest.ManifestVersion}");
                _manifestVersionLabel.Text = $"Manifest: {_manifest.ManifestVersion}";
                BuildSections(_manifest);
                _runAllButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _log.Error("Failed to fetch manifest", ex);
            }
        }

        private void PromptUpdate(UpdateInfo info)
        {
            if (InvokeRequired) { BeginInvoke(new Action<UpdateInfo>(PromptUpdate), info); return; }
            var ans = MessageBox.Show(
                $"A newer Jaminator is available: {info.Version}\n\n" +
                "Download and apply now? The app will restart automatically.",
                "Update available",
                MessageBoxButtons.YesNo, MessageBoxIcon.Information);
            if (ans != DialogResult.Yes) return;

            _ = Task.Run(async () =>
            {
                if (await _updater.ApplyAsync(info))
                {
                    BeginInvoke(new Action(() => { _log.Info("Update applied — exiting for restart"); Application.Exit(); }));
                }
            });
        }

        private void BuildSections(Manifest m)
        {
            _sectionFlow.Controls.Clear();

            if (m.Cleanup != null)
            {
                AddSection("cleanup", "Cleanup",
                    $"{m.Cleanup.TempPaths.Count} temp paths, recycle bin, browser cache, allowlist",
                    () => _cleanup.RunAsync(m.Cleanup, m.Wallpaper));
            }

            if (m.Wallpaper != null)
            {
                AddSection("wallpaper", "Wallpaper",
                    System.IO.Path.GetFileName(m.Wallpaper.Url),
                    () => _wallpaper.EnsureAsync(m.Wallpaper, forceReset: false));
            }

            if (m.Folders.Count > 0)
            {
                AddSection("folders", "Folders",
                    $"{m.Folders.Count} folder(s) under user profile",
                    () => Task.Run(() => _folders.EnsureFolders(m.Folders)));
            }

            if (m.Programs.Count > 0)
            {
                AddSection("programs", "Programs",
                    $"{m.Programs.Count} MSI(s) — installs missing/outdated",
                    () => _msi.InstallAllAsync(m.Programs));
            }

            if (m.Commands.Count > 0)
            {
                AddSection("commands", "Commands",
                    $"{m.Commands.Count} admin command(s)",
                    () => _commands.RunAsync(m.Commands));
            }
        }

        private SectionPanel AddSection(string id, string title, string subtitle, Func<Task>? handler)
        {
            var s = new SectionPanel(id, title);
            s.SetSubtitle(subtitle);
            s.RunHandler = handler;
            _sectionFlow.Controls.Add(s);
            return s;
        }

        private async Task RunAllAsync()
        {
            _runAllButton.Enabled = false;
            _log.Info("");
            _log.Info("=== Run All started ===");

            foreach (var ctrl in _sectionFlow.Controls)
            {
                if (ctrl is SectionPanel s && s.SelectCheckBox.Checked && s.RunButton.Enabled)
                {
                    try { await s.RunOnceAsync(); }
                    catch (Exception ex) { _log.Error("Section failed", ex); }
                }
            }

            _log.Info("=== Run All complete ===");
            _runAllButton.Enabled = true;
        }

        private void UiAppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(UiAppendLog), line); return; }
            _logBox.AppendText(line + Environment.NewLine);
        }
    }
}
