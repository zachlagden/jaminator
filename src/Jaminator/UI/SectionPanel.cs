using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jaminator.UI
{
    /// <summary>
    /// One row in the section list: a coloured accent stripe, checkbox, title,
    /// subtitle, status pill, and Run button.
    /// </summary>
    public sealed class SectionPanel : Panel
    {
        public CheckBox SelectCheckBox { get; }
        public Label TitleLabel { get; }
        public Label SubtitleLabel { get; }
        public Label StatusLabel { get; }
        public Button RunButton { get; }
        public Panel AccentBar { get; }

        public string SectionId { get; }
        public Func<Task>? RunHandler { get; set; }

        /// <summary>Notifies parent when the checkbox state changes.</summary>
        public event EventHandler? CheckedChanged;

        public SectionPanel(string id, string title, Color accent)
        {
            SectionId = id;
            Height = 70;
            Margin = new Padding(8, 0, 8, 8);
            BackColor = Color.FromArgb(40, 40, 40);
            Padding = new Padding(0, 8, 12, 8);

            // Coloured stripe so each section type is visually distinct
            AccentBar = new Panel
            {
                Width = 4,
                Dock = DockStyle.Left,
                BackColor = accent
            };

            SelectCheckBox = new CheckBox
            {
                Checked = true,
                AutoSize = true,
                Location = new Point(16, 24),
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.Transparent,
                Text = ""
            };
            SelectCheckBox.CheckedChanged += (s, e) => CheckedChanged?.Invoke(this, EventArgs.Empty);

            TitleLabel = new Label
            {
                Text = title,
                Location = new Point(40, 12),
                AutoSize = true,
                Font = new Font("Segoe UI", 11F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                BackColor = Color.Transparent
            };

            SubtitleLabel = new Label
            {
                Text = "",
                Location = new Point(40, 36),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F),
                ForeColor = Color.FromArgb(170, 170, 170),
                BackColor = Color.Transparent
            };

            StatusLabel = new Label
            {
                Text = "Ready",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                ForeColor = Color.FromArgb(170, 170, 170),
                BackColor = Color.Transparent
            };

            RunButton = new Button
            {
                Text = "Run",
                Width = 80,
                Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold),
                Cursor = Cursors.Hand
            };
            RunButton.FlatAppearance.BorderSize = 0;
            RunButton.Click += async (_, _) => await RunOnceAsync();

            Controls.Add(AccentBar);
            Controls.Add(SelectCheckBox);
            Controls.Add(TitleLabel);
            Controls.Add(SubtitleLabel);
            Controls.Add(StatusLabel);
            Controls.Add(RunButton);

            Resize += (_, _) => Realign();
            Realign();
        }

        private void Realign()
        {
            RunButton.Location = new Point(Width - RunButton.Width - 12, 18);
            StatusLabel.Location = new Point(RunButton.Left - StatusLabel.Width - 14, 24);
        }

        public void SetSubtitle(string text) => SubtitleLabel.Text = text;

        public void SetStatus(string text, Color color)
        {
            StatusLabel.Text = text;
            StatusLabel.ForeColor = color;
            Realign();
        }

        public void SetEnabled(bool enabled)
        {
            SelectCheckBox.Enabled = enabled;
            RunButton.Enabled = enabled;
            ForeColor = enabled ? Color.FromArgb(240, 240, 240) : Color.FromArgb(120, 120, 120);
        }

        public async Task RunOnceAsync()
        {
            if (RunHandler == null) { SetStatus("Not implemented", Color.FromArgb(160, 160, 160)); return; }
            RunButton.Enabled = false;
            SetStatus("Running…", Color.FromArgb(255, 167, 38));
            try
            {
                await RunHandler();
                SetStatus("Done", Color.FromArgb(76, 175, 80));
            }
            catch (Exception ex)
            {
                SetStatus("Failed", Color.FromArgb(239, 83, 80));
                throw new Exception($"[{SectionId}] {ex.Message}", ex);
            }
            finally
            {
                RunButton.Enabled = true;
            }
        }
    }
}
