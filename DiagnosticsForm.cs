using System;
using System.Drawing;
using System.Windows.Forms;

namespace VgcStatus
{
    internal sealed class DiagnosticsForm : Form
    {
        private static readonly Color ColorBg = Color.FromArgb(22, 24, 29);
        private static readonly Color ColorCard = Color.FromArgb(33, 36, 43);
        private static readonly Color ColorText = Color.FromArgb(230, 232, 236);

        private readonly string _report;
        private readonly Button _copyButton;
        private readonly Timer _copyTimer;

        public DiagnosticsForm(string serviceName, string report)
        {
            _report = report;

            Text = Loc.Of("Диагностика — ", "Diagnostics — ") + serviceName;
            ClientSize = new Size(660, 470);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = ColorBg;
            Font = new Font("Segoe UI", 9f);

            TextBox text = new TextBox();
            text.Multiline = true;
            text.ReadOnly = true;
            text.ScrollBars = ScrollBars.Both;
            text.WordWrap = false;
            text.AcceptsReturn = true;
            text.Font = new Font("Consolas", 9.5f);
            text.BackColor = Color.FromArgb(16, 18, 22);
            text.ForeColor = ColorText;
            text.BorderStyle = BorderStyle.None;
            text.Location = new Point(16, 16);
            text.Size = new Size(628, 390);
            text.Text = report.Replace("\r\n", "\n").Replace("\n", "\r\n");
            text.SelectionStart = 0;
            text.SelectionLength = 0;
            Controls.Add(text);

            _copyButton = new Button();
            _copyButton.Text = Loc.Of("Копировать отчёт", "Copy report");
            _copyButton.Location = new Point(16, 420);
            _copyButton.Size = new Size(170, 34);
            StyleButton(_copyButton, ColorCard, ColorText, 9.5f, false);
            _copyButton.Click += OnCopyClick;
            Controls.Add(_copyButton);

            Button closeButton = new Button();
            closeButton.Text = Loc.Of("Закрыть", "Close");
            closeButton.Location = new Point(474, 420);
            closeButton.Size = new Size(170, 34);
            StyleButton(closeButton, Color.FromArgb(47, 129, 247), Color.White, 9.5f, true);
            closeButton.Click += delegate { Close(); };
            closeButton.DialogResult = DialogResult.OK;
            Controls.Add(closeButton);

            _copyTimer = new Timer();
            _copyTimer.Interval = 1500;
            _copyTimer.Tick += delegate
            {
                _copyTimer.Stop();
                _copyButton.Text = Loc.Of("Копировать отчёт", "Copy report");
            };

            CancelButton = closeButton;
        }

        private static void StyleButton(Button button, Color back, Color fore, float size, bool bold)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        private void OnCopyClick(object sender, EventArgs e)
        {
            try
            {
                Clipboard.SetText(_report);
                _copyButton.Text = Loc.Of("Скопировано", "Copied");
                _copyTimer.Stop();
                _copyTimer.Start();
            }
            catch
            {
                _copyButton.Text = Loc.Of("Не удалось", "Failed");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _copyTimer != null)
            {
                _copyTimer.Stop();
                _copyTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

}
