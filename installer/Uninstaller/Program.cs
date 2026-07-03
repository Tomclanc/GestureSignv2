using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace GestureSign.Uninstaller
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableDpiChangedMessageHandling", true);
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableWindowsFormsHighDpiAutoResizing", true);

            if (!args.Contains("--from-temp", StringComparer.OrdinalIgnoreCase))
            {
                RelaunchFromTemp();
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm());
        }

        private static void RelaunchFromTemp()
        {
            var source = Assembly.GetExecutingAssembly().Location;
            var target = Path.Combine(Path.GetTempPath(), $"GestureSign-Uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(source, target, true);
            Process.Start(new ProcessStartInfo(target, "--from-temp") { UseShellExecute = true });
        }
    }

    internal sealed class UninstallForm : Form
    {
        private readonly bool _isDark = IsDarkTheme();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly Label _status = new Label();
        private readonly CheckBox _deleteAll = new CheckBox();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _uninstallButton = new Button();
        private readonly Button _cancelButton = new Button();
        private bool _completed;

        public UninstallForm()
        {
            Text = "卸载 GestureSign V2";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(580, 360);
            Size = new Size(660, 400);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var back = _isDark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(243, 246, 250);
            var card = _isDark ? Color.FromArgb(43, 45, 50) : Color.FromArgb(252, 253, 255);
            var text = _isDark ? Color.White : Color.FromArgb(32, 32, 32);
            var subText = _isDark ? Color.FromArgb(205, 205, 205) : Color.FromArgb(92, 92, 92);
            var accent = Color.FromArgb(196, 43, 28);

            BackColor = back;
            ForeColor = text;

            var panel = new Panel
            {
                BackColor = card,
                Location = new Point(28, 28),
                Size = new Size(588, 270),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(panel);

            _title.Text = "卸载 GestureSign V2";
            _title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            _title.ForeColor = text;
            _title.AutoSize = false;
            _title.Location = new Point(28, 26);
            _title.Size = new Size(520, 40);
            panel.Controls.Add(_title);

            _subtitle.Text = "默认只删除程序文件，保留手势配置、日志、备份和用户数据。";
            _subtitle.ForeColor = subText;
            _subtitle.AutoSize = false;
            _subtitle.Location = new Point(30, 78);
            _subtitle.Size = new Size(530, 44);
            panel.Controls.Add(_subtitle);

            _deleteAll.Text = "同时删除所有相关文件（配置、日志、备份和安装残留文件）";
            _deleteAll.ForeColor = text;
            _deleteAll.BackColor = card;
            _deleteAll.AutoSize = false;
            _deleteAll.Location = new Point(30, 130);
            _deleteAll.Size = new Size(530, 34);
            panel.Controls.Add(_deleteAll);

            _status.Text = "准备就绪";
            _status.ForeColor = subText;
            _status.AutoSize = false;
            _status.Location = new Point(30, 180);
            _status.Size = new Size(530, 28);
            panel.Controls.Add(_status);

            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Location = new Point(30, 220);
            _progress.Size = new Size(528, 18);
            panel.Controls.Add(_progress);

            _uninstallButton.Text = "卸载";
            _uninstallButton.BackColor = accent;
            _uninstallButton.ForeColor = Color.White;
            _uninstallButton.FlatStyle = FlatStyle.Flat;
            _uninstallButton.FlatAppearance.BorderSize = 0;
            _uninstallButton.Location = new Point(410, 318);
            _uninstallButton.Size = new Size(98, 36);
            _uninstallButton.Click += (_, __) => Uninstall();
            Controls.Add(_uninstallButton);

            _cancelButton.Text = "取消";
            _cancelButton.BackColor = _isDark ? Color.FromArgb(55, 57, 62) : Color.White;
            _cancelButton.ForeColor = text;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.FlatAppearance.BorderColor = _isDark ? Color.FromArgb(78, 80, 86) : Color.FromArgb(214, 218, 224);
            _cancelButton.Location = new Point(518, 318);
            _cancelButton.Size = new Size(98, 36);
            _cancelButton.Click += (_, __) => Close();
            Controls.Add(_cancelButton);
        }

        private async void Uninstall()
        {
            if (_completed)
            {
                Close();
                return;
            }

            var product = FindInstalledGestureSignProducts().FirstOrDefault();
            if (product == null)
            {
                _status.Text = "未找到已安装的 GestureSign V2。";
                return;
            }

            _uninstallButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progress.MarqueeAnimationSpeed = 24;

            try
            {
                var cleanAll = _deleteAll.Checked;
                await System.Threading.Tasks.Task.Run(() =>
                {
                    SetStatus("正在关闭正在运行的 GestureSign...");
                    KillGestureSign();
                    SetStatus(cleanAll ? "正在卸载并删除相关文件..." : "正在卸载，保留用户数据...");
                    RunMsiexec($"/x {product.ProductCode} CLEANALL={(cleanAll ? "1" : "0")} /qn /norestart /L*V \"{NewMsiLogPath("uninstall")}\"");
                });

                _progress.MarqueeAnimationSpeed = 0;
                _status.Text = cleanAll ? "卸载完成，相关文件已清理。" : "卸载完成，用户数据已保留。";
                _completed = true;
                _uninstallButton.Text = "完成";
                _uninstallButton.Enabled = true;
                _cancelButton.Text = "关闭";
                _cancelButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _progress.MarqueeAnimationSpeed = 0;
                _status.Text = "卸载失败：" + ex.Message;
                _uninstallButton.Enabled = true;
                _cancelButton.Enabled = true;
            }
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }
            _status.Text = text;
        }

        private static void KillGestureSign()
        {
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName;
                    var path = SafeProcessPath(process);
                    var isGestureSignProcess = name.StartsWith("GestureSign", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("RestartAgent", StringComparison.OrdinalIgnoreCase)
                        || (!string.IsNullOrWhiteSpace(path)
                            && path.IndexOf("GestureSign V2", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (!isGestureSignProcess || process.Id == Process.GetCurrentProcess().Id)
                        continue;

                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { }
            }
        }

        private static string SafeProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static IReadOnlyList<InstalledProduct> FindInstalledGestureSignProducts()
        {
            var roots = new[]
            {
                Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")
            };

            var products = new List<InstalledProduct>();
            foreach (var root in roots.Where(item => item != null))
            {
                foreach (var subKeyName in root.GetSubKeyNames())
                {
                    using (var key = root.OpenSubKey(subKeyName))
                    {
                        var displayName = key?.GetValue("DisplayName") as string;
                        var windowsInstaller = key?.GetValue("WindowsInstaller")?.ToString();
                        if (!IsGestureSignProduct(displayName) || windowsInstaller != "1")
                            continue;

                        if (subKeyName.StartsWith("{", StringComparison.Ordinal) && subKeyName.EndsWith("}", StringComparison.Ordinal))
                            products.Add(new InstalledProduct(subKeyName, key.GetValue("DisplayVersion") as string ?? "未知版本"));
                    }
                }
            }

            return products
                .GroupBy(item => item.ProductCode, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static void RunMsiexec(string arguments)
        {
            using (var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (process == null)
                    throw new InvalidOperationException("无法启动 Windows Installer。");

                process.WaitForExit();
                if (process.ExitCode != 0 && process.ExitCode != 3010)
                    throw new InvalidOperationException($"Windows Installer 返回 {process.ExitCode}");
            }
        }

        private static string NewMsiLogPath(string name)
            => Path.Combine(Path.GetTempPath(), $"GestureSign-{name}-{DateTime.Now:yyyyMMddHHmmssfff}.log");

        private static bool IsGestureSignProduct(string displayName)
            => string.Equals(displayName, "GestureSign", StringComparison.OrdinalIgnoreCase)
               || string.Equals(displayName, "GestureSign V2", StringComparison.OrdinalIgnoreCase);

        private static bool IsDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class InstalledProduct
    {
        public InstalledProduct(string productCode, string version)
        {
            ProductCode = productCode;
            Version = version;
        }

        public string ProductCode { get; }
        public string Version { get; }
    }
}
