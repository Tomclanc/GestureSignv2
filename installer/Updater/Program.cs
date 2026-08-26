using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace GestureSign.Updater
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableDpiChangedMessageHandling", true);
            AppContext.SetSwitch("Switch.System.Windows.Forms.EnableWindowsFormsHighDpiAutoResizing", true);

            var options = UpdateOptions.Parse(args);
            if (!options.FromTemp)
            {
                RelaunchFromTemp(args);
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new UpdateForm(options));
        }

        private static void RelaunchFromTemp(string[] args)
        {
            var source = Assembly.GetExecutingAssembly().Location;
            var target = Path.Combine(Path.GetTempPath(), $"GestureSign-Updater-{Guid.NewGuid():N}.exe");
            File.Copy(source, target, true);
            var forwarded = args.Concat(new[] { "--from-temp" }).Select(QuoteArgument);
            Process.Start(new ProcessStartInfo(target, string.Join(" ", forwarded))
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetTempPath()
            });
        }

        private static string QuoteArgument(string value)
            => "\"" + (value ?? string.Empty).Replace("\"", "\\\"") + "\"";
    }

    internal sealed class UpdateForm : Form
    {
        private readonly UpdateOptions _options;
        private readonly Label _title = new Label();
        private readonly Label _status = new Label();
        private readonly ProgressBar _progress = new ProgressBar();
        private readonly Button _closeButton = new Button();

        public UpdateForm(UpdateOptions options)
        {
            _options = options;
            Text = "更新 GestureSign V2";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(560, 290);
            Size = new Size(620, 310);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

            var dark = IsDarkTheme();
            var background = dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(243, 246, 250);
            var card = dark ? Color.FromArgb(43, 45, 50) : Color.FromArgb(252, 253, 255);
            var text = dark ? Color.White : Color.FromArgb(32, 32, 32);
            var subText = dark ? Color.FromArgb(205, 205, 205) : Color.FromArgb(92, 92, 92);
            BackColor = background;
            ForeColor = text;

            var panel = new Panel
            {
                BackColor = card,
                Location = new Point(28, 28),
                Size = new Size(548, 190),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            Controls.Add(panel);

            _title.Text = "正在更新 GestureSign V2";
            _title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            _title.ForeColor = text;
            _title.AutoSize = false;
            _title.Location = new Point(28, 26);
            _title.Size = new Size(490, 40);
            panel.Controls.Add(_title);

            _status.Text = "正在等待 GestureSign 退出…";
            _status.ForeColor = subText;
            _status.AutoSize = false;
            _status.Location = new Point(30, 82);
            _status.Size = new Size(490, 44);
            panel.Controls.Add(_status);

            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 24;
            _progress.Location = new Point(30, 140);
            _progress.Size = new Size(488, 18);
            panel.Controls.Add(_progress);

            _closeButton.Text = "取消";
            _closeButton.Enabled = false;
            _closeButton.Location = new Point(478, 232);
            _closeButton.Size = new Size(98, 36);
            _closeButton.Click += (_, __) => Close();
            Controls.Add(_closeButton);

            Shown += async (_, __) => await RunUpdateAsync();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_closeButton.Enabled)
            {
                e.Cancel = true;
                return;
            }
            base.OnFormClosing(e);
        }

        private async Task RunUpdateAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    ValidateOptions();
                    WaitForMainProcess();
                    SetStatus("正在关闭后台服务和快捷操作…");
                    StopTargetProcesses();

                    if (string.Equals(_options.Mode, "msi", StringComparison.OrdinalIgnoreCase))
                        InstallMsi();
                    else
                        ReplacePortableDirectory();
                });

                _progress.MarqueeAnimationSpeed = 0;
                _progress.Value = 100;
                _title.Text = "更新完成";
                _status.Text = "GestureSign V2 已更新，正在重新启动…";
                await Task.Delay(700);
                LaunchUpdatedApplication();
                _closeButton.Enabled = true;
                Close();
            }
            catch (Exception ex)
            {
                _progress.MarqueeAnimationSpeed = 0;
                _title.Text = "更新失败";
                _status.Text = ex.Message;
                _closeButton.Text = "关闭";
                _closeButton.Enabled = true;
            }
            finally
            {
                TryDeleteFile(_options.PackagePath);
            }
        }

        private void ValidateOptions()
        {
            if (!string.Equals(_options.Mode, "msi", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_options.Mode, "portable", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("未知的更新方式。");
            if (string.IsNullOrWhiteSpace(_options.PackagePath) || !File.Exists(_options.PackagePath))
                throw new FileNotFoundException("找不到已下载的更新包。", _options.PackagePath);
            if (string.IsNullOrWhiteSpace(_options.TargetDirectory) || !Directory.Exists(_options.TargetDirectory))
                throw new DirectoryNotFoundException("找不到当前 GestureSign 目录。");
        }

        private void WaitForMainProcess()
        {
            if (_options.WaitProcessId <= 0)
                return;
            try
            {
                using (var process = Process.GetProcessById(_options.WaitProcessId))
                    process.WaitForExit(45000);
            }
            catch
            {
            }
        }

        private void StopTargetProcesses()
        {
            var root = NormalizeDirectory(_options.TargetDirectory);
            foreach (var process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == Process.GetCurrentProcess().Id)
                        continue;
                    var path = process.MainModule?.FileName;
                    if (string.IsNullOrWhiteSpace(path))
                        continue;
                    var fullPath = Path.GetFullPath(path);
                    if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        continue;
                    process.Kill();
                    process.WaitForExit(5000);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private void InstallMsi()
        {
            SetStatus("正在保留已安装的 Kando 可选组件…");
            PreserveBundledKando(_options.TargetDirectory);
            SetStatus("正在覆盖安装新版本…");
            var logPath = Path.Combine(Path.GetTempPath(), $"GestureSign-update-{DateTime.Now:yyyyMMddHHmmssfff}.log");
            var arguments = $"/i \"{_options.PackagePath}\" /passive /norestart /L*V \"{logPath}\"";
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
                    throw new InvalidOperationException($"Windows Installer 返回 {process.ExitCode}。详细日志：{logPath}");
            }
        }

        private void ReplacePortableDirectory()
        {
            SetStatus("正在解压并验证便携版…");
            var target = NormalizeDirectory(_options.TargetDirectory);
            var parent = Directory.GetParent(target)?.FullName ?? throw new InvalidOperationException("无法确定程序目录的上级路径。");
            var staging = Path.Combine(parent, $".{Path.GetFileName(target)}.update-{Guid.NewGuid():N}");
            var backup = Path.Combine(parent, $".{Path.GetFileName(target)}.backup-{Guid.NewGuid():N}");
            var targetMoved = false;

            try
            {
                SetStatus("正在保留已安装的 Kando 可选组件…");
                PreserveBundledKando(target);
                ZipFile.ExtractToDirectory(_options.PackagePath, staging);
                ValidatePortableDirectory(staging);
                SetStatus("正在替换程序文件…");

                Directory.Move(target, backup);
                targetMoved = true;
                Directory.Move(staging, target);
                PreservePortableData(backup, target);
                TryDeleteDirectory(backup);
            }
            catch
            {
                TryDeleteDirectory(staging);
                if (targetMoved && Directory.Exists(backup))
                {
                    try
                    {
                        TryDeleteDirectory(target);
                        Directory.Move(backup, target);
                    }
                    catch
                    {
                    }
                }
                throw;
            }
        }

        private static void ValidatePortableDirectory(string directory)
        {
            foreach (var relative in new[] { "GestureSign.WinUI.exe", @"Backend\GestureSign.exe" })
            {
                if (!File.Exists(Path.Combine(directory, relative)))
                    throw new InvalidDataException("更新包缺少必要文件：" + relative);
            }
        }

        private static void PreserveBundledKando(string applicationDirectory)
        {
            if (string.IsNullOrWhiteSpace(applicationDirectory))
                return;

            var source = Path.Combine(applicationDirectory, "Kando");
            if (!Directory.Exists(source) || !Directory.EnumerateFiles(source, "kando.exe", SearchOption.AllDirectories).Any())
                return;

            var componentsRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestureSign V2",
                "Components");
            var destination = Path.Combine(componentsRoot, "Kando");
            var removedMarker = Path.Combine(componentsRoot, "Kando.removed");
            if (Directory.Exists(destination) || File.Exists(removedMarker))
                return;

            var staging = destination + ".migrate-" + Guid.NewGuid().ToString("N");
            try
            {
                CopyDirectory(source, staging);
                Directory.CreateDirectory(componentsRoot);
                Directory.Move(staging, destination);
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            var sourcePrefixLength = source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length + 1;
            foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(destination, directory.Substring(sourcePrefixLength)));
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(destination, file.Substring(sourcePrefixLength));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static void PreservePortableData(string backup, string target)
        {
            var oldData = Path.Combine(backup, "AppData");
            if (!Directory.Exists(oldData))
                return;
            var newData = Path.Combine(target, "AppData");
            if (Directory.Exists(newData))
                TryDeleteDirectory(newData);
            Directory.Move(oldData, newData);
        }

        private void LaunchUpdatedApplication()
        {
            var executable = Path.Combine(_options.TargetDirectory, _options.LaunchExecutable ?? "GestureSign.WinUI.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("更新完成，但找不到启动程序。", executable);
            Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = _options.TargetDirectory
            });
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

        private static string NormalizeDirectory(string value)
            => Path.GetFullPath(value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch
            {
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
            }
        }

        private static bool IsDarkTheme()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    internal sealed class UpdateOptions
    {
        public string Mode { get; private set; }
        public string PackagePath { get; private set; }
        public string TargetDirectory { get; private set; }
        public string LaunchExecutable { get; private set; }
        public int WaitProcessId { get; private set; }
        public bool FromTemp { get; private set; }

        public static UpdateOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index++)
            {
                var item = args[index];
                if (string.Equals(item, "--from-temp", StringComparison.OrdinalIgnoreCase))
                {
                    values["from-temp"] = "true";
                    continue;
                }
                if (!item.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                    continue;
                values[item.Substring(2)] = args[++index];
            }

            int.TryParse(Get(values, "wait-pid"), out var waitPid);
            return new UpdateOptions
            {
                Mode = Get(values, "mode"),
                PackagePath = Get(values, "package"),
                TargetDirectory = Get(values, "target"),
                LaunchExecutable = Get(values, "launch"),
                WaitProcessId = waitPid,
                FromTemp = values.ContainsKey("from-temp")
            };
        }

        private static string Get(IReadOnlyDictionary<string, string> values, string key)
            => values.TryGetValue(key, out var value) ? value : string.Empty;
    }
}
