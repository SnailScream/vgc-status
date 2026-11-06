using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VgcStatus
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string serviceName = "vgc";
            if (args.Length > 0 && args[0].Trim().Length > 0)
            {
                serviceName = args[0].Trim();
            }

            Application.Run(new MainForm(serviceName));
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color ColorBg = Color.FromArgb(22, 24, 29);
        private static readonly Color ColorCard = Color.FromArgb(33, 36, 43);
        private static readonly Color ColorText = Color.FromArgb(230, 232, 236);
        private static readonly Color ColorMuted = Color.FromArgb(140, 147, 160);
        private static readonly Color ColorGreen = Color.FromArgb(63, 185, 80);
        private static readonly Color ColorRed = Color.FromArgb(248, 81, 73);
        private static readonly Color ColorAmber = Color.FromArgb(210, 153, 34);
        private static readonly Color ColorAccent = Color.FromArgb(47, 129, 247);
        private static readonly Color ColorDisabled = Color.FromArgb(48, 52, 60);
        private static readonly Color ColorDivider = Color.FromArgb(58, 63, 74);

        private const string VgtrayProcess = "vgtray";
        private const string VanguardDir = @"C:\Program Files\Riot Vanguard";
        private const string VgtrayPath = VanguardDir + @"\vgtray.exe";

        private readonly string _serviceName;
        private readonly Label _vgcCaption;
        private readonly Label _vgtrayCaption;
        private readonly Label _vgcStatus;
        private readonly Label _vgtrayStatus;
        private readonly Label _detailLabel;
        private readonly Button _startButton;
        private readonly Button _vgtrayButton;
        private readonly Button _refreshButton;
        private readonly Button _diagnosticsButton;
        private readonly Button _languageButton;
        private readonly Timer _timer;
        private bool _busy;

        public MainForm(string serviceName)
        {
            _serviceName = serviceName;

            ClientSize = new Size(420, 324);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = ColorBg;
            Font = new Font("Segoe UI", 9f);
            Icon = SystemIcons.Shield;

            _languageButton = new Button();
            _languageButton.FlatStyle = FlatStyle.Flat;
            _languageButton.FlatAppearance.BorderSize = 0;
            _languageButton.BackColor = ColorBg;
            _languageButton.ForeColor = ColorMuted;
            _languageButton.Font = new Font("Segoe UI", 8.5f);
            _languageButton.TextAlign = ContentAlignment.MiddleRight;
            _languageButton.Cursor = Cursors.Hand;
            _languageButton.UseVisualStyleBackColor = false;
            _languageButton.Location = new Point(330, 6);
            _languageButton.Size = new Size(70, 20);
            _languageButton.Click += OnLanguageClick;
            Controls.Add(_languageButton);

            Panel card = new Panel();
            card.BackColor = ColorCard;
            card.Location = new Point(20, 34);
            card.Size = new Size(380, 176);
            Controls.Add(card);

            _vgcCaption = MakeCaption("VGC  •  СЛУЖБА", new Point(16, 12));
            card.Controls.Add(_vgcCaption);

            _vgcStatus = MakeStatusLabel(new Point(14, 32));
            card.Controls.Add(_vgcStatus);

            _vgtrayCaption = MakeCaption("VGTRAY  •  ПРОЦЕСС", new Point(206, 12));
            card.Controls.Add(_vgtrayCaption);

            _vgtrayStatus = MakeStatusLabel(new Point(204, 32));
            card.Controls.Add(_vgtrayStatus);

            Panel divider = new Panel();
            divider.BackColor = ColorDivider;
            divider.Location = new Point(190, 16);
            divider.Size = new Size(1, 72);
            card.Controls.Add(divider);

            _startButton = CreateButton("Запустить", new Point(16, 104), new Size(162, 34), ColorAccent, Color.White, 9.5f);
            _startButton.Click += OnStartClick;
            card.Controls.Add(_startButton);

            _vgtrayButton = CreateButton("Запустить vgtray", new Point(202, 104), new Size(162, 34), ColorAccent, Color.White, 9.5f);
            _vgtrayButton.Click += OnVgtrayClick;
            card.Controls.Add(_vgtrayButton);

            _detailLabel = new Label();
            _detailLabel.Text = "…";
            _detailLabel.Font = new Font("Segoe UI", 8.5f);
            _detailLabel.ForeColor = ColorMuted;
            _detailLabel.AutoSize = true;
            _detailLabel.Location = new Point(16, 150);
            card.Controls.Add(_detailLabel);

            _refreshButton = CreateButton("Обновить", new Point(20, 224), new Size(380, 36), ColorCard, ColorText, 9.5f);
            _refreshButton.Click += delegate { RefreshStatus(); };
            Controls.Add(_refreshButton);

            _diagnosticsButton = CreateButton("Почему не запускается? — подробный отчёт",
                new Point(20, 272), new Size(380, 34),
                Color.FromArgb(28, 31, 37), ColorMuted, 9f);
            _diagnosticsButton.Click += OnDiagnosticsClick;
            Controls.Add(_diagnosticsButton);

            _timer = new Timer();
            _timer.Interval = 2000;
            _timer.Tick += delegate { RefreshStatus(); };
            _timer.Start();

            ApplyLanguage();
        }

        private void OnLanguageClick(object sender, EventArgs e)
        {
            Loc.En = !Loc.En;
            ApplyLanguage();
        }

        private void ApplyLanguage()
        {
            Text = Loc.Of("Vanguard — статус", "Vanguard — status");

            _vgcCaption.Text = Loc.Of("VGC  •  СЛУЖБА", "VGC  •  SERVICE");
            _vgtrayCaption.Text = Loc.Of("VGTRAY  •  ПРОЦЕСС", "VGTRAY  •  PROCESS");

            _startButton.Text = Loc.Of("Запустить", "Start");
            _vgtrayButton.Text = Loc.Of("Запустить vgtray", "Start vgtray");
            _refreshButton.Text = Loc.Of("Обновить", "Refresh");
            _diagnosticsButton.Text = Loc.Of("Почему не запускается? — подробный отчёт",
                                             "Why won't it start? — full report");

            // Кнопка показывает язык, на который переключит интерфейс.
            _languageButton.Text = Loc.Of("Language", "Язык");

            RefreshStatus();
        }

        private static Label MakeCaption(string text, Point location)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", 8.25f, FontStyle.Bold);
            label.ForeColor = ColorMuted;
            label.AutoSize = true;
            label.Location = location;
            return label;
        }

        private static Label MakeStatusLabel(Point location)
        {
            Label label = new Label();
            label.Text = "…";
            label.Font = new Font("Segoe UI", 15f, FontStyle.Bold);
            label.ForeColor = ColorMuted;
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Size = new Size(162, 32);
            label.Location = location;
            return label;
        }

        private static Button CreateButton(string text, Point location, Size size, Color back, Color fore, float fontSize)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = location;
            button.Size = size;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = back;
            button.ForeColor = fore;
            button.Font = new Font("Segoe UI", fontSize, FontStyle.Bold);
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = true;
            return button;
        }

        private void RefreshStatus()
        {
            if (_busy) return;

            string vgcText;
            Color vgcColor;
            string detailOverride = null;
            bool canStartVgc = false;

            try
            {
                using (ServiceController sc = new ServiceController(_serviceName))
                {
                    ServiceControllerStatus status = sc.Status;
                    canStartVgc = status == ServiceControllerStatus.Stopped;

                    switch (status)
                    {
                        case ServiceControllerStatus.Running:
                            vgcText = Loc.Of("РАБОТАЕТ", "RUNNING");
                            vgcColor = ColorGreen;
                            break;
                        case ServiceControllerStatus.Stopped:
                            vgcText = Loc.Of("ОСТАНОВЛЕНА", "STOPPED");
                            vgcColor = ColorRed;
                            break;
                        case ServiceControllerStatus.StartPending:
                            vgcText = Loc.Of("ЗАПУСКАЕТСЯ…", "STARTING…");
                            vgcColor = ColorAmber;
                            break;
                        case ServiceControllerStatus.StopPending:
                            vgcText = Loc.Of("ОСТАНОВКА…", "STOPPING…");
                            vgcColor = ColorAmber;
                            break;
                        case ServiceControllerStatus.Paused:
                            vgcText = Loc.Of("ПАУЗА", "PAUSED");
                            vgcColor = ColorAmber;
                            break;
                        case ServiceControllerStatus.PausePending:
                            vgcText = Loc.Of("ПАУЗА…", "PAUSING…");
                            vgcColor = ColorAmber;
                            break;
                        case ServiceControllerStatus.ContinuePending:
                            vgcText = Loc.Of("ПРОДОЛЖЕНИЕ…", "RESUMING…");
                            vgcColor = ColorAmber;
                            break;
                        default:
                            vgcText = status.ToString().ToUpperInvariant();
                            vgcColor = ColorMuted;
                            break;
                    }
                }
            }
            catch (InvalidOperationException)
            {
                vgcText = Loc.Of("НЕ НАЙДЕНА", "NOT FOUND");
                vgcColor = ColorMuted;
                detailOverride = Loc.Of("Служба «" + _serviceName + "» не установлена на этом ПК",
                                        "Service \"" + _serviceName + "\" is not installed on this PC");
            }
            catch (Exception ex)
            {
                vgcText = Loc.Of("ОШИБКА", "ERROR");
                vgcColor = ColorRed;
                detailOverride = Short(ex.Message);
            }

            _vgcStatus.Text = vgcText;
            _vgcStatus.ForeColor = vgcColor;
            SetButtonState(_startButton, canStartVgc);

            bool vgtrayFile = false;
            try
            {
                vgtrayFile = File.Exists(VgtrayPath);
            }
            catch
            {
            }

            bool vgtrayRunning = IsVgtrayRunning();
            string vgtrayText;
            Color vgtrayColor;

            if (vgtrayRunning)
            {
                vgtrayText = Loc.Of("РАБОТАЕТ", "RUNNING");
                vgtrayColor = ColorGreen;
            }
            else if (vgtrayFile)
            {
                vgtrayText = Loc.Of("НЕ ЗАПУЩЕН", "NOT RUNNING");
                vgtrayColor = ColorRed;
            }
            else
            {
                vgtrayText = Loc.Of("НЕ НАЙДЕН", "NOT FOUND");
                vgtrayColor = ColorMuted;
            }

            _vgtrayStatus.Text = vgtrayText;
            _vgtrayStatus.ForeColor = vgtrayColor;
            SetButtonState(_vgtrayButton, vgtrayFile && !vgtrayRunning);

            if (detailOverride != null)
            {
                _detailLabel.Text = detailOverride;
                _detailLabel.ForeColor = ColorMuted;
            }
            else if (!vgtrayRunning && vgtrayFile)
            {
                _detailLabel.Text = Loc.Of("vgtray не запущен — нажмите «Запустить vgtray»",
                                           "vgtray is not running — press \"Start vgtray\"");
                _detailLabel.ForeColor = ColorAmber;
            }
            else
            {
                _detailLabel.Text = string.Format(
                    Loc.Of("Обновлено: {0}   •   автопроверка каждые 2 с",
                           "Updated: {0}   •   auto-check every 2 s"),
                    DateTime.Now.ToString("HH:mm:ss"));
                _detailLabel.ForeColor = ColorMuted;
            }
        }

        private static void SetButtonState(Button button, bool enabled)
        {
            button.Enabled = enabled;
            button.BackColor = enabled ? ColorAccent : ColorDisabled;
        }

        private static bool IsVgtrayRunning()
        {
            Process[] processes = null;
            try
            {
                processes = Process.GetProcessesByName(VgtrayProcess);
                return processes.Length > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (processes != null)
                {
                    foreach (Process process in processes)
                    {
                        process.Dispose();
                    }
                }
            }
        }

        private async void OnStartClick(object sender, EventArgs e)
        {
            _busy = true;
            _startButton.Enabled = false;
            _vgcStatus.Text = Loc.Of("ЗАПУСК…", "STARTING…");
            _vgcStatus.ForeColor = ColorAmber;
            _detailLabel.Text = Loc.Of("Отправлена команда запуска, ожидание до 30 с…",
                                       "Start command sent, waiting up to 30 s…");
            _detailLabel.ForeColor = ColorMuted;

            Exception failure = null;

            try
            {
                await Task.Run(delegate
                {
                    using (ServiceController sc = new ServiceController(_serviceName))
                    {
                        sc.Refresh();
                        if (sc.Status != ServiceControllerStatus.Stopped)
                        {
                            return;
                        }

                        sc.Start();
                        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));
                    }
                });
            }
            catch (Exception ex)
            {
                failure = ex;
            }

            try
            {
                if (failure != null)
                {
                    await ShowDiagnostics(failure);
                }
            }
            finally
            {
                _busy = false;
                RefreshStatus();
            }
        }

        private async void OnVgtrayClick(object sender, EventArgs e)
        {
            if (!File.Exists(VgtrayPath))
            {
                MessageBox.Show(this,
                    string.Format(Loc.Of(
                        "Файл не найден:\n{0}\n\nПохоже, Vanguard не установлен на этом ПК.",
                        "File not found:\n{0}\n\nVanguard does not appear to be installed on this PC."),
                        VgtrayPath),
                    "vgtray", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _vgtrayButton.Enabled = false;
            _detailLabel.Text = Loc.Of("Запускаю vgtray…", "Starting vgtray…");
            _detailLabel.ForeColor = ColorMuted;

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = VgtrayPath;
                startInfo.WorkingDirectory = VanguardDir;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);

                _detailLabel.Text = Loc.Of("vgtray запущен, проверяю статусы…",
                                           "vgtray started, checking statuses…");
            }
            catch (Exception ex)
            {
                _detailLabel.Text = Loc.Of("vgtray не запустился", "vgtray failed to start");
                _detailLabel.ForeColor = ColorRed;
                MessageBox.Show(this,
                    string.Format(Loc.Of("Не удалось запустить vgtray:\n{0}",
                                         "Failed to start vgtray:\n{0}"), ex.Message),
                    Loc.Of("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            await Task.Delay(3000);
            RefreshStatus();
        }

        private async void OnDiagnosticsClick(object sender, EventArgs e)
        {
            await ShowDiagnostics(null);
        }

        private async Task ShowDiagnostics(Exception failure)
        {
            _busy = true;
            _diagnosticsButton.Enabled = false;
            Cursor = Cursors.WaitCursor;

            try
            {
                string report = await Task.Run(delegate
                {
                    return ServiceDiagnostics.BuildReport(_serviceName, failure);
                });

                using (DiagnosticsForm dialog = new DiagnosticsForm(_serviceName, report))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    Loc.Of("Не удалось собрать диагностику: ", "Failed to build diagnostics: ") + ex.Message,
                    Loc.Of("Ошибка", "Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
                _diagnosticsButton.Enabled = true;
                _busy = false;
                RefreshStatus();
            }
        }

        private static string Short(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ");
            return text.Length > 70 ? text.Substring(0, 67) + "…" : text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
