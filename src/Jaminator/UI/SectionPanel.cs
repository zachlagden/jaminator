using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Jaminator.UI
{
    /// <summary>
    /// One row in the section list: checkbox, title, status pill, Run button.
    /// </summary>
    public sealed class SectionPanel : Panel
    {
        public CheckBox SelectCheckBox { get; }
        public Label TitleLabel { get; }
        public Label SubtitleLabel { get; }
        public Label StatusLabel { get; }
        public Button RunButton { get; }

        public string SectionId { get; }

        /// <summary>The work to do when this section runs. Set by MainForm.</summary>
        public Func<Task>? RunHandler { get; set; }

        public SectionPanel(string id, string title)
        {
            SectionId = id;
            Height = 64;
            Margin = new Padding(0, 0, 0, 6);
            BackColor = Color.FromArgb(40, 40, 40);
            Padding = new Padding(12, 8, 12, 8);
            Dock = DockStyle.Top;

            SelectCheckBox = new CheckBox
            {
                Checked = true,
                AutoSize = true,
                Location = new Point(12, 22),
                ForeColor = Color.FromArgb(220, 220, 220),
                BackColor = Color.Transparent,
                Text = ""
            };

            TitleLabel = new Label
            {
                Text = title,
                Location = new Point(36, 8),
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                ForeColor = Color.FromArgb(240, 240, 240),
                BackColor = Color.Transparent
            };

            SubtitleLabel = new Label
            {
                Text = "",
                Location = new Point(36, 30),
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F),
                ForeColor = Color.FromArgb(160, 160, 160),
                BackColor = Color.Transparent
            };

            StatusLabel = new Label
            {
                Text = "Ready",
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                AutoSize = true,
                Font = new Font("Segoe UI", 8.5F, FontStyle.Bold),
                ForeColor = Color.FromArgb(160, 160, 160),
                BackColor = Color.Transparent
            };

            RunButton = new Button
            {
                Text = "Run",
                Width = 80,
                Height = 30,
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            RunButton.FlatAppearance.BorderSize = 0;
            RunButton.Click += async (_, _) => await RunOnceAsync();

            Controls.Add(SelectCheckBox);
            Controls.Add(TitleLabel);
            Controls.Add(SubtitleLabel);
            Controls.Add(StatusLabel);
            Controls.Add(RunButton);

            Resize += (_, _) => Layout();
            Layout();
        }

        private new void Layout()
        {
            RunButton.Location = new Point(Width - RunButton.Width - 12, 16);
            StatusLabel.Location = new Point(RunButton.Left - StatusLabel.Width - 12, 22);
        }

        public void SetSubtitle(string text)
        {
            SubtitleLabel.Text = text;
        }

        public void SetStatus(string text, Color color)
        {
            StatusLabel.Text = text;
            StatusLabel.ForeColor = color;
            Layout();
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
