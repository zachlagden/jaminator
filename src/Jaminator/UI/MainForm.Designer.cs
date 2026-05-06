#nullable enable
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Jaminator.UI
{
    partial class MainForm
    {
        private IContainer? components = null;

        private Panel _headerBar = null!;
        private Label _titleLabel = null!;
        private Label _manifestVersionLabel = null!;
        private Button _installButton = null!;
        private Button _uninstallButton = null!;

        private SplitContainer _split = null!;
        private Panel _sectionsHost = null!;
        private TableLayoutPanel _sectionsTable = null!;
        private Label _autoHeader = null!;
        private Label _autoSubhead = null!;
        private Label _manualHeader = null!;
        private Label _manualSubhead = null!;
        private FlowLayoutPanel _autoFlow = null!;
        private FlowLayoutPanel _manualFlow = null!;

        private TextBox _logBox = null!;
        private Label _logLabel = null!;
        private Button _runAllButton = null!;

        private Panel _offlineOverlay = null!;
        private Label _offlineTitle = null!;
        private Label _offlineSubtitle = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();

            // ---------- Header ----------
            _headerBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 56,
                BackColor = Color.FromArgb(28, 28, 30)
            };

            _titleLabel = new Label
            {
                Text = "Jaminator " + Program.ToolVersion,
                Font = new Font("Segoe UI", 14F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Location = new Point(16, 0),
                Size = new Size(300, 56)
            };

            _manifestVersionLabel = new Label
            {
                Text = "Manifest: loading…",
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(180, 180, 180),
                BackColor = Color.Transparent,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Size = new Size(300, 56),
                Location = new Point(0, 0)
            };

            _installButton = new Button
            {
                Text = "Install to system",
                Width = 140, Height = 32,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _installButton.FlatAppearance.BorderSize = 0;

            _uninstallButton = new Button
            {
                Text = "Uninstall",
                Width = 100, Height = 32,
                BackColor = Color.FromArgb(80, 50, 50),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                Cursor = Cursors.Hand
            };
            _uninstallButton.FlatAppearance.BorderSize = 0;

            _headerBar.Controls.Add(_titleLabel);
            _headerBar.Controls.Add(_manifestVersionLabel);
            _headerBar.Controls.Add(_installButton);
            _headerBar.Controls.Add(_uninstallButton);
            _headerBar.Resize += (_, _) =>
            {
                Button? activeBtn = _installButton.Visible ? _installButton :
                                   _uninstallButton.Visible ? _uninstallButton : null;
                if (activeBtn != null)
                    activeBtn.Location = new Point(_headerBar.Width - activeBtn.Width - 16, 12);
                var vrEnd = activeBtn != null ? activeBtn.Left : _headerBar.Width - 16;
                _manifestVersionLabel.Location = new Point(vrEnd - _manifestVersionLabel.Width - 12, 0);
            };

            // ---------- Sections (top half of split) ----------
            _autoHeader = MakeGroupHeader("Automatic on logon", Color.FromArgb(76, 175, 80));
            _autoSubhead = MakeGroupSubhead(
                "Idempotent fix-ups. Already running every time someone logs in via the scheduled task.");

            _manualHeader = MakeGroupHeader("Manual — only runs when you click", Color.FromArgb(239, 83, 80));
            _manualSubhead = MakeGroupSubhead(
                "Disruptive: deletes files, installs software, applies registry tweaks. Never auto-runs during a lesson.");

            _autoFlow = MakeFlow();
            _manualFlow = MakeFlow();

            _sectionsTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 20, 20),
                Padding = new Padding(0, 8, 0, 8),
                Margin = new Padding(0)
            };
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 16));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            _sectionsTable.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _sectionsTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            _sectionsTable.Controls.Add(_autoHeader,   0, 0);
            _sectionsTable.Controls.Add(_autoSubhead,  0, 1);
            _sectionsTable.Controls.Add(_autoFlow,     0, 2);
            _sectionsTable.Controls.Add(MakeSpacer(),  0, 3);
            _sectionsTable.Controls.Add(_manualHeader, 0, 4);
            _sectionsTable.Controls.Add(_manualSubhead,0, 5);
            _sectionsTable.Controls.Add(_manualFlow,   0, 6);

            _sectionsHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(20, 20, 20)
            };
            _sectionsHost.Controls.Add(_sectionsTable);

            // ---------- Log (bottom half) ----------
            _logLabel = new Label
            {
                Dock = DockStyle.Top,
                Height = 28,
                Text = "  Log",
                TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(200, 200, 200),
                BackColor = Color.FromArgb(28, 28, 30)
            };
            _logBox = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(15, 15, 15),
                ForeColor = Color.FromArgb(220, 220, 220),
                BorderStyle = BorderStyle.None
            };

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                BackColor = Color.FromArgb(28, 28, 30),
                SplitterWidth = 4,
                FixedPanel = FixedPanel.None
            };
            _split.Panel1.Controls.Add(_sectionsHost);
            _split.Panel2.Controls.Add(_logBox);
            _split.Panel2.Controls.Add(_logLabel);

            // ---------- Footer ----------
            _runAllButton = new Button
            {
                Text = "Run All Selected",
                Dock = DockStyle.Bottom,
                Height = 56,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                Cursor = Cursors.Hand
            };
            _runAllButton.FlatAppearance.BorderSize = 0;

            // ---------- Form ----------
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(1000, 740);
            Controls.Add(_split);
            Controls.Add(_runAllButton);
            Controls.Add(_headerBar);
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(820, 560);
            Text = "Jaminator";
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(20, 20, 20);

            Shown += (_, _) =>
            {
                _split.SplitterDistance = (int)(_split.Height * 0.62);
            };

            // ---------- Offline overlay ----------
            _offlineOverlay = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(245, 25, 25, 30),
                Visible = false
            };
            _offlineTitle = new Label
            {
                Text = "No internet connection",
                Font = new Font("Segoe UI", 18F, FontStyle.Bold),
                ForeColor = Color.FromArgb(239, 83, 80),
                AutoSize = true,
                BackColor = Color.Transparent
            };
            _offlineSubtitle = new Label
            {
                Text = "Jaminator needs internet to fetch its manifest.\nRetrying every 10 seconds.",
                Font = new Font("Segoe UI", 10F),
                ForeColor = Color.FromArgb(220, 220, 220),
                AutoSize = true,
                TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.Transparent
            };
            _offlineOverlay.Controls.Add(_offlineTitle);
            _offlineOverlay.Controls.Add(_offlineSubtitle);
            _offlineOverlay.Resize += (_, _) =>
            {
                _offlineTitle.Location = new Point(
                    (_offlineOverlay.Width - _offlineTitle.Width) / 2,
                    (_offlineOverlay.Height / 2) - 50);
                _offlineSubtitle.Location = new Point(
                    (_offlineOverlay.Width - _offlineSubtitle.Width) / 2,
                    (_offlineOverlay.Height / 2) + 8);
            };
            // Add LAST so it sits on top of everything else
            Controls.Add(_offlineOverlay);
            _offlineOverlay.BringToFront();
        }

        private static Label MakeGroupHeader(string text, Color accent)
        {
            return new Label
            {
                Text = "  " + text,
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 8, 8, 0),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = accent,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static Label MakeGroupSubhead(string text)
        {
            return new Label
            {
                Text = "  " + text,
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 0, 8, 6),
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(150, 150, 150),
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };
        }

        private static FlowLayoutPanel MakeFlow()
        {
            var f = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Padding = new Padding(0),
                BackColor = Color.Transparent
            };
            f.ControlAdded += (s, e) =>
            {
                e.Control.Width = f.ClientSize.Width - e.Control.Margin.Horizontal - 4;
            };
            f.Resize += (s, e) =>
            {
                foreach (Control c in f.Controls)
                    c.Width = f.ClientSize.Width - c.Margin.Horizontal - 4;
            };
            return f;
        }

        private static Panel MakeSpacer()
        {
            return new Panel { Dock = DockStyle.Fill, Height = 16, BackColor = Color.Transparent, Margin = new Padding(0) };
        }
    }
}
