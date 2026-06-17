using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestureSign.Setup
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableDpiChangedMessageHandling", true);
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableWindowsFormsHighDpiAutoResizing", true);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupForm());
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly bool _isDark = IsDarkTheme();
        private readonly Label _title = new Label();
        private readonly Label _subtitle = new Label();
        private readonly Label _status = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _installButton = new Button();
        private readonly Button _cancelButton = new Button();
        private bool _completed;

        public SetupForm()
        {
            Text = "GestureSign 安装";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 340);
            Size = new Size(640, 380);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var back = _isDark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(243, 246, 250);
            var card = _isDark ? Color.FromArgb(43, 45, 50) : Color.FromArgb(252, 253, 255);
            var text = _isDark ? Color.White : Color.FromArgb(32, 32, 32);
            var subText = _isDark ? Color.FromArgb(205, 205, 205) : Color.FromArgb(92, 92, 92);
            var accent = Color.FromArgb(0, 120, 212);

            BackColor = back;
            ForeColor = text;

            var panel = new Panel
            {
                BackColor = card,
                Location = new Point(28, 28),
                Size = new Size(568, 260),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(panel);

            _title.Text = "安装 GestureSign";
            _title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            _title.ForeColor = text;
            _title.AutoSize = false;
            _title.Location = new Point(28, 28);
            _title.Size = new Size(500, 40);
            panel.Controls.Add(_title);

            _subtitle.Text = "将自动清理旧版本并安装最新版。安装过程中不会修改你的手势配置。";
            _subtitle.ForeColor = subText;
            _subtitle.AutoSize = false;
            _subtitle.Location = new Point(30, 78);
            _subtitle.Size = new Size(500, 48);
            panel.Controls.Add(_subtitle);

            _status.Text = "准备就绪";
            _status.ForeColor = subText;
            _status.AutoSize = false;
            _status.Location = new Point(30, 148);
            _status.Size = new Size(500, 28);
            panel.Controls.Add(_status);

            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 0;
            _progress.Location = new Point(30, 188);
            _progress.Size = new Size(508, 18);
            panel.Controls.Add(_progress);

            _installButton.Text = "安装";
            _installButton.BackColor = accent;
            _installButton.ForeColor = Color.White;
            _installButton.FlatStyle = FlatStyle.Flat;
            _installButton.FlatAppearance.BorderSize = 0;
            _installButton.Location = new Point(390, 306);
            _installButton.Size = new Size(98, 36);
            _installButton.Click += async (_, __) => await InstallAsync();
            Controls.Add(_installButton);

            _cancelButton.Text = "取消";
            _cancelButton.BackColor = _isDark ? Color.FromArgb(55, 57, 62) : Color.White;
            _cancelButton.ForeColor = text;
            _cancelButton.FlatStyle = FlatStyle.Flat;
            _cancelButton.FlatAppearance.BorderColor = _isDark ? Color.FromArgb(78, 80, 86) : Color.FromArgb(214, 218, 224);
            _cancelButton.Location = new Point(498, 306);
            _cancelButton.Size = new Size(98, 36);
            _cancelButton.Click += (_, __) => Close();
            Controls.Add(_cancelButton);
        }

        private async Task InstallAsync()
        {
            if (_completed)
            {
                Close();
                return;
            }

            _installButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progress.MarqueeAnimationSpeed = 24;

            try
            {
                var msiPath = ExtractMsi();
                await Task.Run(() =>
                {
                    SetStatus("正在关闭正在运行的 GestureSign...");
                    KillGestureSign();

                    var products = FindInstalledGestureSignProducts();
                    foreach (var product in products)
                    {
                        SetStatus($"正在移除旧版本 {product.Version}...");
                        RunMsiexec($"/x {product.ProductCode} /qn /norestart");
                    }

                    SetStatus("正在安装最新版...");
                    RunMsiexec($"/i \"{msiPath}\" /qn /norestart");
                });

                _progress.MarqueeAnimationSpeed = 0;
                _status.Text = "安装完成。可以从桌面或开始菜单打开 GestureSign 设置。";
                _completed = true;
                _installButton.Text = "完成";
                _installButton.Enabled = true;
                _cancelButton.Text = "关闭";
                _cancelButton.Enabled = true;
            }
            catch (Exception ex)
            {
                _progress.MarqueeAnimationSpeed = 0;
                _status.Text = "安装失败：" + ex.Message;
                _installButton.Enabled = true;
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

        private static string ExtractMsi()
        {
            var path = Path.Combine(Path.GetTempPath(), "GestureSign-Setup-x64.msi");
            using (var input = Assembly.GetExecutingAssembly().GetManifestResourceStream("GestureSign-Setup-x64.msi"))
            {
                if (input == null)
                    throw new FileNotFoundException("安装包资源缺失。");
                using (var output = File.Create(path))
                    input.CopyTo(output);
            }
            return path;
        }

        private static void KillGestureSign()
        {
            foreach (var name in new[] { "GestureSign", "GestureSign.WinUI" })
            {
                foreach (var process in Process.GetProcessesByName(name))
                {
                    try
                    {
                        process.Kill();
                        process.WaitForExit(3000);
                    }
                    catch { }
                }
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
