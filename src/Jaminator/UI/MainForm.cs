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
        /// <summary>
        /// Sections that run automatically at user logon. Everything else is
        /// gated behind a tech clicking a button — we never disrupt a lesson
        /// by wiping temp / installing software / running registry tweaks
        /// without explicit consent.
        /// </summary>
        private static readonly HashSet<string> LoginSafeSections = new HashSet<string>
        {
            "wallpaper",
            "folders"
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

        private Manifest? _manifest;

        public MainForm()
        {
            // The form is shown only in interactive mode. Login-mode hides it
            // immediately on creation so we get the message pump for HttpClient
            // without actually flashing a window at the user.
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

            UpdateInstallButtonVisibility();

            Load += OnLoad;
            _runAllButton.Click += async (_, _) => await RunAllAsync();
            _installButton.Click += OnInstallClick;
        }

        private void UpdateInstallButtonVisibility()
        {
            if (Installer.IsInstalled || Installer.IsRunningFromInstallDir)
            {
                _installButton.Visible = false;
            }
            else
            {
                _installButton.Visible = true;
            }
        }

        private async void OnLoad(object? sender, EventArgs e)
        {
            _log.Info($"Jaminator v{Program.ToolVersion} ({Program.Mode})");
            _log.Info($"Architecture: {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")} Windows");

            // Self-update only runs in interactive mode — at logon we never want
            // a self-update dialog popping up unexpectedly.
            if (Program.Mode == Program.RunMode.Ui)
            {
                _ = Task.Run(async () =>
                {
                    var info = await _updater.CheckAsync(Program.ToolVersion);
                    if (info != null) PromptUpdate(info);
                });
            }

            _log.Info($"Fetching manifest: {Program.ManifestUrl}");
            try
            {
                _manifest = await _fetcher.FetchAsync(Program.ManifestUrl);
                _log.Info($"Manifest version: {_manifest.ManifestVersion}");
                _manifestVersionLabel.Text = $"Manifest: {_manifest.ManifestVersion}";
                BuildSections(_manifest);
                _runAllButton.Enabled = true;

                if (Program.RunAllOnStart)
                {
                    if (Program.LoginModeOnly)
                        _log.Info("Login mode: running login-safe sections only (folders, wallpaper)");
                    else
                        _log.Info("CLI flag --run-all: executing all sections automatically");

                    await RunAllAsync();

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
                    BeginInvoke(new Action(() =>
                    {
                        _log.Info("Update applied — exiting for restart");
                        Application.Exit();
                    }));
                }
            });
        }

        private void OnInstallClick(object? sender, EventArgs e)
        {
            var ans = MessageBox.Show(
                $"Install Jaminator to {Installer.InstallDir} and register the auto-logon scheduled task?\n\n" +
                "After install, the login-safe sections (folders, wallpaper) will run automatically " +
                "on every user logon. Cleanup, programs, and admin commands stay manual.",
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
                    UpdateInstallButtonVisibility();
                    if (rc == 0)
                        MessageBox.Show("Installed. Open from Start Menu to run things manually.",
                                        "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }));
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
                    $"{m.Programs.Count} installer(s) — installs missing/outdated",
                    () => _msi.InstallAllAsync(m.Programs));
            }

            if (m.Commands.Count > 0)
            {
                AddSection("commands", "Commands",
                    $"{m.Commands.Count} admin command(s)",
                    () => _commands.RunAsync(m.Commands));
            }

            // In login mode, dim the disruptive sections so the log clearly shows
            // they were intentionally skipped.
            if (Program.LoginModeOnly)
            {
                foreach (var ctrl in _sectionFlow.Controls)
                {
                    if (ctrl is SectionPanel s && !LoginSafeSections.Contains(s.SectionId))
                    {
                        s.SelectCheckBox.Checked = false;
                        s.SetStatus("Login-skip", Color.FromArgb(120, 120, 120));
                    }
                }
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
            _log.Info(Program.LoginModeOnly
                ? "=== Login auto-run started ==="
                : "=== Run All started ===");

            foreach (var ctrl in _sectionFlow.Controls)
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
            _runAllButton.Enabled = true;
        }

        private void UiAppendLog(string line)
        {
            if (InvokeRequired) { BeginInvoke(new Action<string>(UiAppendLog), line); return; }
            _logBox.AppendText(line + Environment.NewLine);
        }
    }
}
