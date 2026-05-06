using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using Jaminator.Models;
using Jaminator.Services;

namespace Jaminator.UI
{
    internal sealed partial class MainForm : Form
    {
        // Sections that the scheduled task runs at every user logon. Everything
        // else is manual — only fires when the tech clicks. Anything missing
        // here is treated as "manual / disruptive" and goes in the bottom group.
        private static readonly HashSet<string> LoginSafeSections = new HashSet<string>
        {
            "wallpaper",
            "folders"
        };

        // Per-section accent colour + one-line description. Keeps the UI honest
        // about what each action does so techs can pick safely.
        private static readonly Dictionary<string, (Color Accent, string Help)> SectionStyle =
            new Dictionary<string, (Color, string)>
            {
                ["cleanup"]   = (Color.FromArgb(255, 152, 0),  "Wipes temp files, clears browser caches, quarantines stray Documents items."),
                ["wallpaper"] = (Color.FromArgb(76, 175, 80),  "Downloads + applies the canonical Jam Coding wallpaper. Reverts kid-changes."),
                ["folders"]   = (Color.FromArgb(76, 175, 80),  "Creates the school folder layout under Documents/."),
                ["programs"]  = (Color.FromArgb(0, 150, 199),  "Installs/updates educational software. Skips programs already on the laptop."),
                ["commands"]  = (Color.FromArgb(239, 83, 80),  "Admin scripts (disable Cortana, kill OneDrive, remove Microsoft bloatware…).")
            };

        private readonly Logger _log;
        private readonly ManifestFetcher _fetcher = new ManifestFetcher();
        private readonly Downloader _downloader;
        private readonly FolderManager _folders;
        private readonly WallpaperSetter _wallpaper;
        private readonly CleanupRunner _cleanup;
        private readonly CommandRunner _commands;
        private readonly MsiInstaller _msi;
        private readonly SelfUpdater _updater;
        private readonly InternetGate _gate;

        private Manifest? _manifest;

        public MainForm()
        {
            InitializeComponent();
            if (Program.LoginModeOnly)
            {
                ShowInTaskbar = false;
                WindowState = FormWindowState.Minimized;
                Opacity = 0;
            }

            _log = new Logger();
            _log.OnMessage += UiAppendLog;

            _downloader = new Downloader(_log);
            _folders = new FolderManager(_log);
            _wallpaper = new WallpaperSetter(_log, _downloader);
            _cleanup = new CleanupRunner(_log, _wallpaper);
            _commands = new CommandRunner(_log);
            _msi = new MsiInstaller(_log, _downloader);
            _updater = new SelfUpdater(_log);
            _gate = new InternetGate(Program.ManifestUrl, _log);

            UpdateHeaderButtonsVisibility();

            Load += OnLoad;
            _runAllButton.Click += async (_, _) => await RunAllAsync();
            _installButton.Click += OnInstallClick;
            _uninstallButton.Click += OnUninstallClick;
            _checkUpdatesButton.Click += async (_, _) => await OnCheckUpdatesClickAsync();
        }

        private void UpdateHeaderButtonsVisibility()
        {
            _installButton.Visible = !Installer.IsInstalled;
            _uninstallButton.Visible = Installer.IsInstalled;
        }

        private void SetOfflineOverlay(bool show)
        {
            _offlineOverlay.Visible = show;
            if (show) _offlineOverlay.BringToFront();
            // Block all action buttons while offline so the user can't trigger
            // any download/install that's guaranteed to fail.
            _runAllButton.Enabled = !show && _manifest != null;
            _installButton.Enabled = !show;
            _uninstallButton.Enabled = !show;
        }

        private async void OnLoad(object? sender, EventArgs e)
        {
            _log.Info($"Jaminator v{Program.ToolVersion} ({Program.Mode})");
            _log.Info($"Architecture: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} Windows");

            // Block until we can actually reach GitHub. Without internet, every
            // section is broken (manifest fetch fails, downloads fail) — better
            // to show a clear "no internet" state than half-fail everywhere.
            //
            // UI mode: poll forever — there's a real human watching the overlay.
            // CLI modes: bound the wait so a scheduled run never hangs the box.
            var maxWait = (Program.Mode == Program.RunMode.RunAll || Program.LoginModeOnly)
                ? (TimeSpan?)TimeSpan.FromMinutes(5)
                : null;
            var online = await _gate.WaitUntilOnlineAsync(o =>
            {
                if (InvokeRequired) { BeginInvoke(new Action(() => SetOfflineOverlay(!o))); return; }
                SetOfflineOverlay(!o);
            }, maxWait);

            if (!online)
            {
                _log.Error("No network — exiting (CLI mode, max wait elapsed)");
                if (Program.ExitAfterRun) { Application.Exit(); return; }
            }

            // Auto-update only in interactive UI mode. Logon-time auto-runs must
            // never trigger a Windows Installer dialog while a kid is logging in.
            if (Program.Mode == Program.RunMode.Ui)
            {
                _ = Task.Run(async () =>
                {
                    var info = await _updater.CheckAsync(Program.ToolVersion);
                    if (info != null) AutoApplyUpdate(info);
                });
            }

            _log.Info($"Fetching manifest: {Program.ManifestUrl}");
            try
            {
                _manifest = await _fetcher.FetchAsync(Program.ManifestUrl);
                _log.Info($"Manifest version: {_manifest.ManifestVersion}");
                _manifestVersionLabel.Text = $"Manifest: {_manifest.ManifestVersion}";
                BuildSections(_manifest);
                UpdateRunAllButtonText();
                _runAllButton.Enabled = true;

                if (Program.RunAllOnStart)
                {
                    if (Program.LoginModeOnly)
                        _log.Info("Login mode: running login-safe sections only (folders, wallpaper)");
                    else
                        _log.Info("CLI flag --run-all: executing all sections automatically");

                    await RunAllAsync();

                    // Login-mode also reconciles the daily auto-run task per the manifest's
                    // schedule.dailyRunAll setting. Doing this every login means changing
                    // the time in manifest.json propagates to every laptop without ceremony.
                    if (Program.LoginModeOnly && _manifest.Schedule != null)
                    {
                        Installer.ReconcileDailyTask(_manifest.Schedule.DailyRunAll, _log);
                    }

                    if (Program.ExitAfterRun)
                    {
                        _log.Info("Exiting (CLI mode)");
                        Application.Exit();
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error("Failed to fetch manifest", ex);
                if (Program.ExitAfterRun) Application.Exit();
            }
        }

        private void AutoApplyUpdate(UpdateInfo info)
        {
            if (InvokeRequired) { BeginInvoke(new Action<UpdateInfo>(AutoApplyUpdate), info); return; }

            _log.Info($"Update available: {info.Version} — applying automatically");
            // Toast-like in-app notice; no modal dialog so the tool still works
            // for them if they happen to be mid-task when this fires.
            Text = $"Jaminator — updating to {info.Version}…";

            _ = Task.Run(async () =>
            {
                if (await _updater.ApplyAsync(info))
                {
                    BeginInvoke(new Action(() =>
                    {
                        _log.Info("Update launched — exiting so MSI can replace files");
                        Application.Exit();
                    }));
                }
                else
                {
                    BeginInvoke(new Action(() =>
                    {
                        Text = "Jaminator";
                        _log.Warn("Auto-update failed — continuing on current version");
                    }));
                }
            });
        }

        private void OnInstallClick(object? sender, EventArgs e)
        {
            var ans = MessageBox.Show(
                $"Install Jaminator to {Installer.InstallDir} and register the auto-logon scheduled task?\n\n" +
                "After install, the login-safe sections (Wallpaper, Folders) will run automatically " +
                "on every user logon. Cleanup, Programs, and Commands stay manual.",
                "Install Jaminator",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ans != DialogResult.Yes) return;

            _installButton.Enabled = false;
            _ = Task.Run(() =>
            {
                var rc = Installer.Install(_log);
                BeginInvoke(new Action(() =>
                {
                    _installButton.Enabled = true;
                    UpdateHeaderButtonsVisibility();
                    if (rc == 0)
                        MessageBox.Show("Installed. Open from Start Menu to run things manually.",
                                        "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
            });
        }

        private async Task OnCheckUpdatesClickAsync()
        {
            _checkUpdatesButton.Enabled = false;
            var prevText = _checkUpdatesButton.Text;
            _checkUpdatesButton.Text = "Checking…";
            try
            {
                var info = await _updater.CheckAsync(Program.ToolVersion);
                if (info != null)
                {
                    _log.Info($"Update available: {info.Version} — applying");
                    AutoApplyUpdate(info);
                }
                else
                {
                    MessageBox.Show($"You're on the latest version ({Program.ToolVersion}).",
                                    "Up to date", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            finally
            {
                _checkUpdatesButton.Text = prevText;
                _checkUpdatesButton.Enabled = true;
            }
        }

        private void OnUninstallClick(object? sender, EventArgs e)
        {
            var ans = MessageBox.Show(
                $"Uninstall Jaminator from {Installer.InstallDir}?\n\n" +
                "Removes the scheduled task and Start Menu shortcut. Logs in C:\\ProgramData\\Jaminator\\ are kept.",
                "Uninstall Jaminator",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (ans != DialogResult.Yes) return;

            _uninstallButton.Enabled = false;
            _ = Task.Run(() =>
            {
                var rc = Installer.Uninstall(_log);
                BeginInvoke(new Action(() =>
                {
                    _uninstallButton.Enabled = true;
                    UpdateHeaderButtonsVisibility();
                    if (rc == 0)
                    {
                        MessageBox.Show("Uninstalled. Closing.", "Done",
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Application.Exit();
                    }
                }));
            });
        }

        private void BuildSections(Manifest m)
        {
            _autoFlow.Controls.Clear();
            _manualFlow.Controls.Clear();

            // The order here determines the "Run All" execution order too.
            if (m.Wallpaper != null)
            {
                AddSection("wallpaper", "Wallpaper",
                    System.IO.Path.GetFileName(m.Wallpaper.Url),
                    () => _wallpaper.EnsureAsync(m.Wallpaper, forceReset: false));
            }
            if (m.Folders.Count > 0)
            {
                AddSection("folders", "Folders",
                    string.Join(", ", m.Folders.ConvertAll(f => f.Path)),
                    () => Task.Run(() => _folders.EnsureFolders(m.Folders)));
            }
            if (m.Cleanup != null)
            {
                AddSection("cleanup", "Cleanup",
                    $"{m.Cleanup.TempPaths.Count} temp paths · recycle bin · browser caches · Documents allowlist",
                    () => _cleanup.RunAsync(m.Cleanup, m.Wallpaper));
            }
            if (m.Programs.Count > 0)
            {
                var names = string.Join(", ", m.Programs.ConvertAll(p => p.Name));
                AddSection("programs", "Programs",
                    $"{m.Programs.Count} installer(s): {names}",
                    () => _msi.InstallAllAsync(m.Programs));
            }
            if (m.Commands.Count > 0)
            {
                var names = string.Join(", ", m.Commands.ConvertAll(c => c.Name));
                AddSection("commands", "Commands",
                    $"{m.Commands.Count}: {names}",
                    () => _commands.RunAsync(m.Commands));
            }

            if (Program.LoginModeOnly)
            {
                foreach (FlowLayoutPanel flow in new[] { _autoFlow, _manualFlow })
                foreach (Control ctrl in flow.Controls)
                {
                    if (ctrl is SectionPanel s && !LoginSafeSections.Contains(s.SectionId))
                    {
                        s.SelectCheckBox.Checked = false;
                        s.SetStatus("Login-skip", Color.FromArgb(120, 120, 120));
                    }
                }
            }
        }

        private void AddSection(string id, string title, string subtitle, Func<Task>? handler)
        {
            var (accent, help) = SectionStyle.TryGetValue(id, out var s)
                ? s
                : (Color.FromArgb(120, 120, 120), "");

            var panel = new SectionPanel(id, title, accent);
            // Single-line subtitle that fits in the panel's available width.
            // Help text (one sentence) is preferred; manifest detail goes in tooltip.
            panel.SetSubtitle(string.IsNullOrEmpty(help) ? subtitle : help);
            new ToolTip().SetToolTip(panel.SubtitleLabel,
                string.IsNullOrEmpty(subtitle) ? help : subtitle);

            panel.RunHandler = handler;
            panel.CheckedChanged += (_, _) => UpdateRunAllButtonText();

            var target = LoginSafeSections.Contains(id) ? _autoFlow : _manualFlow;
            target.Controls.Add(panel);
        }

        private void UpdateRunAllButtonText()
        {
            int n = 0;
            foreach (FlowLayoutPanel flow in new[] { _autoFlow, _manualFlow })
                foreach (Control ctrl in flow.Controls)
                    if (ctrl is SectionPanel s && s.SelectCheckBox.Checked && s.RunButton.Enabled) n++;

            _runAllButton.Text = n == 0 ? "Nothing selected" : $"Run {n} section" + (n == 1 ? "" : "s");
            _runAllButton.Enabled = n > 0 && _manifest != null;
        }

        private async Task RunAllAsync()
        {
            _runAllButton.Enabled = false;
            _log.Info("");
            _log.Info(Program.LoginModeOnly
                ? "=== Login auto-run started ==="
                : "=== Run All started ===");

            foreach (FlowLayoutPanel flow in new[] { _autoFlow, _manualFlow })
            foreach (Control ctrl in flow.Controls)
            {
                if (ctrl is SectionPanel s && s.SelectCheckBox.Checked && s.RunButton.Enabled)
                {
                    if (Program.LoginModeOnly && !LoginSafeSections.Contains(s.SectionId))
                        continue;

                    try { await s.RunOnceAsync(); }
                    catch (Exception ex) { _log.Error("Section failed", ex); }
                }
            }

            _log.Info(Program.LoginModeOnly
                ? "=== Login auto-run complete ==="
                : "=== Run All complete ===");
            UpdateRunAllButtonText();
        }

        private void UiAppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(UiAppendLog), line); return; }
            _logBox.AppendText(line + Environment.NewLine);
        }
    }
}
