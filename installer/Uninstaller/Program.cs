using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
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
                RelaunchFromTemp(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location));
                return;
            }

            Environment.CurrentDirectory = Path.GetTempPath();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UninstallForm(GetArgument(args, "--source-dir")));
        }

        private static void RelaunchFromTemp(string sourceDirectory)
        {
            var source = Assembly.GetExecutingAssembly().Location;
            var target = Path.Combine(Path.GetTempPath(), $"GestureSign-Uninstall-{Guid.NewGuid():N}.exe");
            File.Copy(source, target, true);
            Process.Start(new ProcessStartInfo(target, "--from-temp --source-dir " + QuoteArgument(sourceDirectory))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath()
            });
        }

        private static string GetArgument(string[] args, string name)
        {
            for (var index = 0; index + 1 < args.Length; index++)
            {
                if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                    return args[index + 1];
            }

            return string.Empty;
        }

        private static string QuoteArgument(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
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
        private readonly string _sourceDirectory;
        private bool _completed;

        public UninstallForm(string sourceDirectory)
        {
            _sourceDirectory = NormalizeDirectory(sourceDirectory);
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

            if (IsPortableDirectory(_sourceDirectory))
                _status.Text = "已检测到便携版：" + _sourceDirectory;
        }

        private async void Uninstall()
        {
            if (_completed)
            {
                Close();
                return;
            }

            var products = FindInstalledGestureSignProducts();
            var product = products.FirstOrDefault(item => SameDirectory(item.InstallLocation, _sourceDirectory));
            var portableDirectory = product == null && IsPortableDirectory(_sourceDirectory)
                ? _sourceDirectory
                : null;
            if (product == null && portableDirectory == null)
            {
                _status.Text = "未找到已安装的 GestureSign V2，也未检测到有效的便携版目录。";
                return;
            }

            _uninstallButton.Enabled = false;
            _cancelButton.Enabled = false;
            _progress.Style = ProgressBarStyle.Marquee;
            _progress.Value = 0;
            _progress.MarqueeAnimationSpeed = 24;

            try
            {
                var cleanAll = _deleteAll.Checked;
                await System.Threading.Tasks.Task.Run(() =>
                {
                    SetStatus("正在关闭正在运行的 GestureSign...");
                    KillGestureSign(portableDirectory);
                    if (cleanAll)
                        KillGestureSign();
                    SetStatus(cleanAll ? "正在卸载并删除相关文件..." : "正在卸载，保留用户数据...");
                    if (portableDirectory != null)
                        UninstallPortable(portableDirectory, cleanAll);
                    else
                        RunMsiexec($"/x {product.ProductCode} CLEANALL={(cleanAll ? "1" : "0")} /qn /norestart /L*V \"{NewMsiLogPath("uninstall")}\"");
                });

                _progress.MarqueeAnimationSpeed = 0;
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Value = 100;
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
                _progress.Style = ProgressBarStyle.Blocks;
                _progress.Value = 0;
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

        private static void KillGestureSign(string targetDirectory = null)
        {
            var normalizedTarget = NormalizeDirectory(targetDirectory);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    var name = process.ProcessName;
                    var path = SafeProcessPath(process);
                    var isGestureSignProcess = string.IsNullOrWhiteSpace(normalizedTarget)
                        ? name.StartsWith("GestureSign", StringComparison.OrdinalIgnoreCase)
                          || name.Equals("RestartAgent", StringComparison.OrdinalIgnoreCase)
                          || (!string.IsNullOrWhiteSpace(path)
                              && path.IndexOf("GestureSign V2", StringComparison.OrdinalIgnoreCase) >= 0)
                        : IsPathInside(path, normalizedTarget);
                    if (!isGestureSignProcess || process.Id == Process.GetCurrentProcess().Id)
                        continue;

                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch { }
            }
        }

        private static void UninstallPortable(string targetDirectory, bool cleanAll)
        {
            ValidatePortableDirectory(targetDirectory);
            var appData = Path.Combine(targetDirectory, "AppData");
            var parent = Directory.GetParent(targetDirectory)?.FullName
                ?? throw new InvalidOperationException("无法确定便携版目录的上级路径。");
            var preservedAppData = string.Empty;

            try
            {
                if (!cleanAll && Directory.Exists(appData))
                {
                    preservedAppData = Path.Combine(parent, $".GestureSign-AppData-{Guid.NewGuid():N}");
                    Directory.Move(appData, preservedAppData);
                }

                DeleteDirectoryWithRetry(targetDirectory);
                if (!string.IsNullOrWhiteSpace(preservedAppData) && Directory.Exists(preservedAppData))
                {
                    Directory.CreateDirectory(targetDirectory);
                    Directory.Move(preservedAppData, Path.Combine(targetDirectory, "AppData"));
                }

                if (cleanAll)
                    DeleteSharedUserData();
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(preservedAppData) && Directory.Exists(preservedAppData))
                {
                    Directory.CreateDirectory(targetDirectory);
                    var restoredAppData = Path.Combine(targetDirectory, "AppData");
                    if (!Directory.Exists(restoredAppData))
                        Directory.Move(preservedAppData, restoredAppData);
                }
                throw;
            }
        }

        private static void DeleteSharedUserData()
        {
            var paths = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GestureSign V2"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GestureSign V2")
            };
            foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Directory.Exists(path))
                    DeleteDirectoryWithRetry(path);
            }

            using (var software = Registry.CurrentUser.OpenSubKey("Software", writable: true))
                software?.DeleteSubKeyTree("GestureSign V2", throwOnMissingSubKey: false);
        }

        private static void DeleteDirectoryWithRetry(string path)
        {
            Exception lastException = null;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    if (Directory.Exists(path))
                    {
                        ClearReadOnlyAttributes(path);
                        Directory.Delete(path, recursive: true);
                    }
                    return;
                }
                catch (IOException ex) { lastException = ex; }
                catch (UnauthorizedAccessException ex) { lastException = ex; }

                Thread.Sleep(150 * (attempt + 1));
            }

            throw new IOException($"无法删除文件夹“{path}”。请关闭正在访问该文件夹的资源管理器或其他程序后重试。", lastException);
        }

        private static void ClearReadOnlyAttributes(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attributes = File.GetAttributes(file);
                    if ((attributes & FileAttributes.ReadOnly) != 0)
                        File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
                catch { }
            }
        }

        private static void ValidatePortableDirectory(string directory)
        {
            if (!IsPortableDirectory(directory))
                throw new InvalidOperationException("目标目录不是有效的 GestureSign V2 便携版目录，已停止删除。");

            var root = Path.GetPathRoot(directory);
            if (string.Equals(directory, NormalizeDirectory(root), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不能卸载磁盘根目录。");
        }

        private static bool IsPortableDirectory(string directory)
            => !string.IsNullOrWhiteSpace(directory)
               && Directory.Exists(directory)
               && File.Exists(Path.Combine(directory, "GestureSign.WinUI.exe"))
               && File.Exists(Path.Combine(directory, "GestureSign-Uninstall.exe"))
               && File.Exists(Path.Combine(directory, "Backend", "GestureSign.exe"));

        private static bool SameDirectory(string left, string right)
            => !string.IsNullOrWhiteSpace(left)
               && !string.IsNullOrWhiteSpace(right)
               && string.Equals(NormalizeDirectory(left), NormalizeDirectory(right), StringComparison.OrdinalIgnoreCase);

        private static bool IsPathInside(string path, string directory)
            => !string.IsNullOrWhiteSpace(path)
               && !string.IsNullOrWhiteSpace(directory)
               && Path.GetFullPath(path).StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        private static string NormalizeDirectory(string path)
            => string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

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
                            products.Add(new InstalledProduct(
                                subKeyName,
                                key.GetValue("DisplayVersion") as string ?? "未知版本",
                                key.GetValue("InstallLocation") as string ?? string.Empty));
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
        public InstalledProduct(string productCode, string version, string installLocation)
        {
            ProductCode = productCode;
            Version = version;
            InstallLocation = installLocation;
        }

        public string ProductCode { get; }
        public string Version { get; }
        public string InstallLocation { get; }
    }
}
