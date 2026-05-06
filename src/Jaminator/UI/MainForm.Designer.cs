#nullable enable
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace Jaminator.UI
{
    partial class MainForm
    {
        private IContainer? components = null;

        private TextBox _logBox = null!;
        private Button _runAllButton = null!;
        private Label _headerLabel = null!;

        protected override void Dispose(bool disposing)
        {
            if (disposing) components?.Dispose();
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new Container();
            _headerLabel = new Label();
            _logBox = new TextBox();
            _runAllButton = new Button();

            // header
            _headerLabel.AutoSize = false;
            _headerLabel.Dock = DockStyle.Top;
            _headerLabel.Height = 48;
            _headerLabel.TextAlign = ContentAlignment.MiddleLeft;
            _headerLabel.Padding = new Padding(16, 0, 0, 0);
            _headerLabel.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            _headerLabel.Text = "Jaminator";
            _headerLabel.BackColor = Color.FromArgb(32, 33, 36);
            _headerLabel.ForeColor = Color.White;

            // log
            _logBox.Multiline = true;
            _logBox.ReadOnly = true;
            _logBox.ScrollBars = ScrollBars.Vertical;
            _logBox.Font = new Font("Consolas", 9F);
            _logBox.Dock = DockStyle.Fill;
            _logBox.BackColor = Color.FromArgb(20, 20, 20);
            _logBox.ForeColor = Color.FromArgb(220, 220, 220);
            _logBox.BorderStyle = BorderStyle.None;

            // run all
            _runAllButton.Text = "Run All";
            _runAllButton.Dock = DockStyle.Bottom;
            _runAllButton.Height = 48;
            _runAllButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
            _runAllButton.BackColor = Color.FromArgb(0, 120, 215);
            _runAllButton.ForeColor = Color.White;
            _runAllButton.FlatStyle = FlatStyle.Flat;
            _runAllButton.FlatAppearance.BorderSize = 0;
            _runAllButton.Enabled = false;
            _runAllButton.Click += OnRunAllClick;

            // form
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(900, 600);
            Controls.Add(_logBox);
            Controls.Add(_runAllButton);
            Controls.Add(_headerLabel);
            Font = new Font("Segoe UI", 9F);
            MinimumSize = new Size(700, 450);
            Text = "Jaminator";
            StartPosition = FormStartPosition.CenterScreen;
        }
    }
}
