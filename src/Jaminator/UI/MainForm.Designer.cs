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
        private SplitContainer _split = null!;
        private FlowLayoutPanel _sectionFlow = null!;
        private TextBox _logBox = null!;
        private Button _runAllButton = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();

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
                Width = 140,
                Height = 32,
                BackColor = Color.FromArgb(60, 60, 65),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            _installButton.FlatAppearance.BorderSize = 0;

            _headerBar.Controls.Add(_titleLabel);
            _headerBar.Controls.Add(_manifestVersionLabel);
            _headerBar.Controls.Add(_installButton);
            _headerBar.Resize += (_, _) =>
            {
                _installButton.Location = new Point(_headerBar.Width - _installButton.Width - 16, 12);
                var vrEnd = _installButton.Visible ? _installButton.Left : _headerBar.Width - 16;
                _manifestVersionLabel.Location = new Point(vrEnd - _manifestVersionLabel.Width - 12, 0);
            };

            _sectionFlow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoScroll = true,
                BackColor = Color.FromArgb(20, 20, 20),
                Padding = new Padding(8)
            };
            // Make children fill width
            _sectionFlow.Resize += (_, _) =>
            {
                foreach (Control c in _sectionFlow.Controls)
                    c.Width = _sectionFlow.ClientSize.Width - c.Margin.Horizontal - 16;
            };
            _sectionFlow.ControlAdded += (_, e) =>
            {
                e.Control.Width = _sectionFlow.ClientSize.Width - e.Control.Margin.Horizontal - 16;
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
            _split.Panel1.Controls.Add(_sectionFlow);
            _split.Panel2.Controls.Add(_logBox);

            _runAllButton = new Button
            {
                Text = "Run All Selected",
                Dock = DockStyle.Bottom,
                Height = 52,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false
            };
            _runAllButton.FlatAppearance.BorderSize = 0;

            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(960, 680);
            Controls.Add(_split);
            Controls.Add(_runAllButton);
            Controls.Add(_headerBar);
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(720, 480);
            Text = "Jaminator";
            StartPosition = FormStartPosition.CenterScreen;
            Shown += (_, _) => _split.SplitterDistance = (int)(_split.Height * 0.55);
        }
    }
}
