using static GestureSign.Updater.UpdateText;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
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
            SetCulture(options.CultureName);
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
        private int? _msiExitCode;
        private string _msiLogPath;

        public UpdateForm(UpdateOptions options)
        {
            _options = options;
            Text = T("Update GestureSign V2");
            RightToLeft = System.Globalization.CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? RightToLeft.Yes : RightToLeft.No;
            RightToLeftLayout = RightToLeft == RightToLeft.Yes;
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

            _title.Text = T("Updating GestureSign V2");
            _title.Font = new Font(Font.FontFamily, 18F, FontStyle.Bold);
            _title.ForeColor = text;
            _title.AutoSize = false;
            _title.Location = new Point(28, 26);
            _title.Size = new Size(490, 40);
            panel.Controls.Add(_title);

            _status.Text = T("Waiting for GestureSign to exit…");
            _status.ForeColor = subText;
            _status.AutoSize = false;
            _status.Location = new Point(30, 82);
            _status.Size = new Size(490, 78);
            panel.Controls.Add(_status);

            _progress.Style = ProgressBarStyle.Marquee;
            _progress.MarqueeAnimationSpeed = 24;
            _progress.Location = new Point(30, 164);
            _progress.Size = new Size(488, 18);
            panel.Controls.Add(_progress);

            _closeButton.Text = T("Cancel");
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
                    if (!string.IsNullOrWhiteSpace(_options.ReadyPath)) File.WriteAllText(_options.ReadyPath, "ready");
                    WaitForMainProcess();
                    SetStatus(T("Closing the background service and quick actions…"));
                    StopTargetProcesses();

                    if (string.Equals(_options.Mode, "msi", StringComparison.OrdinalIgnoreCase))
                        InstallMsi();
                    else
                        ReplacePortableDirectory();
                });

                _progress.MarqueeAnimationSpeed = 0;
                _progress.Value = 100;
                _title.Text = T("Update completed");
                _status.Text = T("GestureSign V2 has been updated. Restarting…");
                await Task.Delay(700);
                try
                {
                    LaunchUpdatedApplication();
                }
                catch (Exception ex)
                {
                    var status = string.Format(T("GestureSign V2 was updated, but could not restart automatically. Open it from the Start menu or the installation folder. Details: {0}"), ex.Message);
                    if (_msiExitCode.HasValue)
                        Trace.TraceWarning("MSI installed (exit {0}, log {1}); relaunch failed: {2}", _msiExitCode, _msiLogPath, ex);
                    else
                        Trace.TraceWarning(status);
                    _status.Text = status;
                    EnableCloseButton();
                    return;
                }
                _closeButton.Enabled = true;
                Close();
            }
            catch (Exception ex)
            {
                _progress.MarqueeAnimationSpeed = 0;
                _title.Text = T("Update failed");
                _status.Text = ex.Message;
                EnableCloseButton();
            }
            finally
            {
                TryDeleteFile(_options.PackagePath);
            }
        }

        private void EnableCloseButton()
        {
            _closeButton.Text = T("Close");
            _closeButton.Enabled = true;
        }

        private void ValidateOptions()
        {
            if (!string.Equals(_options.Mode, "msi", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(_options.Mode, "portable", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(T("Unknown update mode."));
            if (string.IsNullOrWhiteSpace(_options.PackagePath) || !File.Exists(_options.PackagePath))
                throw new FileNotFoundException(T("The downloaded update package was not found."), _options.PackagePath);
            if (string.IsNullOrWhiteSpace(_options.TargetDirectory) || !Directory.Exists(_options.TargetDirectory))
                throw new DirectoryNotFoundException(T("The current GestureSign directory was not found."));
        }

        private void WaitForMainProcess()
        {
            if (_options.WaitProcessId <= 0)
                return;
            try
            {
                using (var process = Process.GetProcessById(_options.WaitProcessId))
                    if (!process.WaitForExit(45000))
                        throw new TimeoutException(T("GestureSign is still running; update was cancelled before replacing files."));
            }
            catch (ArgumentException)
            {
                // The original process already exited.
            }
        }

        private void StopTargetProcesses()
        {
            var root = NormalizeDirectory(_options.TargetDirectory);
            // The settings window brings the daemon back when it disappears, so a single pass
            // can leave a fresh process running and lock the files we are about to replace.
            for (var round = 0; round < 5; round++)
            {
                if (!KillProcessesUnder(root))
                    return;
                Thread.Sleep(300);
            }
        }

        /// <summary>Kills every process whose image lives under <paramref name="root"/>; returns whether any was killed.</summary>
        private static bool KillProcessesUnder(string root)
        {
            var killed = false;
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
                    killed = true;
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
            return killed;
        }

        private void InstallMsi()
        {
            SetStatus(T("Preserving the installed radial menu component…"));
            PreserveBundledKando(_options.TargetDirectory);
            SetStatus(T("Installing the new version…"));
            _msiLogPath = Path.Combine(Path.GetTempPath(), $"GestureSign-update-{DateTime.Now:yyyyMMddHHmmssfff}.log");
            // GS_SKIP_START keeps the freshly installed build from launching while the
            // updater still has files to replace; the updater starts it afterwards.
            var arguments = $"/i \"{_options.PackagePath}\" /passive /norestart GS_SKIP_START=1 /L*V \"{_msiLogPath}\"";
            using (var process = Process.Start(new ProcessStartInfo("msiexec.exe", arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }))
            {
                if (process == null)
                    throw new InvalidOperationException(T("Could not start Windows Installer."));
                process.WaitForExit();
                _msiExitCode = process.ExitCode;
                if (_msiExitCode != 0 && _msiExitCode != 3010)
                    throw new InvalidOperationException(string.Format(T("Windows Installer returned {0}. Detailed log: {1}"), _msiExitCode, _msiLogPath));
            }
        }

        private void ReplacePortableDirectory()
        {
            SetStatus(T("Extracting and validating the portable update…"));
            var target = NormalizeDirectory(_options.TargetDirectory);
            var parent = Directory.GetParent(target)?.FullName ?? throw new InvalidOperationException(T("Could not determine the parent application directory."));
            var staging = Path.Combine(parent, $".{Path.GetFileName(target)}.update-{Guid.NewGuid():N}");
            var backup = Path.Combine(parent, $".{Path.GetFileName(target)}.backup-{Guid.NewGuid():N}");
            var targetMoved = false;

            try
            {
                SetStatus(T("Preserving the installed radial menu component…"));
                PreserveBundledKando(target);
                ZipFile.ExtractToDirectory(_options.PackagePath, staging);
                ValidatePortableDirectory(staging);
                SetStatus(T("Replacing application files…"));

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
            foreach (var relative in new[] { "GestureSignV2.WinUI.exe", @"Backend\GestureSignV2Daemon.exe" })
            {
                if (!File.Exists(Path.Combine(directory, relative)))
                    throw new InvalidDataException(string.Format(T("The update package is missing a required file: {0}"), relative));
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
            var msi = string.Equals(_options.Mode, "msi", StringComparison.OrdinalIgnoreCase);
            string registeredDirectory = msi ? UpdateLaunch.GetMsiInstallLocation(_options.PackagePath) : null;
            var executable = UpdateLaunch.ResolveExecutable(_options.TargetDirectory, registeredDirectory, msi);
            if (!File.Exists(executable))
                throw new FileNotFoundException(string.Format(T("The application executable was not found: {0}"), executable), executable);
            using (var process = Process.Start(new ProcessStartInfo(executable)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(executable)
            }))
            {
                if (process == null)
                    throw new InvalidOperationException(T("Could not start GestureSign V2."));
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

    internal static class UpdateLaunch
    {
        private const string ExecutableName = "GestureSignV2.WinUI.exe";

        internal static string ResolveExecutable(string targetDirectory, string registeredDirectory, bool msi)
        {
            // Only a fully qualified local drive path from the exact installed
            // package can override the launch root. Never scan for matching names.
            var directory = msi && IsLocalAbsoluteDirectory(registeredDirectory)
                ? registeredDirectory : targetDirectory;
            return Path.Combine(Path.GetFullPath(directory), ExecutableName);
        }

        private static bool IsLocalAbsoluteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path.Length < 3 ||
                !((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z')) ||
                path[1] != ':' || (path[2] != Path.DirectorySeparatorChar && path[2] != Path.AltDirectorySeparatorChar) || path.IndexOf(':', 2) >= 0)
                return false;
            try { return Path.IsPathRooted(Path.GetFullPath(path)); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (PathTooLongException) { return false; }
        }

        internal static string GetMsiInstallLocation(string packagePath)
        {
            uint database = 0, view = 0, record = 0;
            try
            {
                if (MsiOpenDatabase(packagePath, IntPtr.Zero, out database) != 0 ||
                    MsiDatabaseOpenView(database, "SELECT `Value` FROM `Property` WHERE `Property` = 'ProductCode'", out view) != 0 ||
                    MsiViewExecute(view, 0) != 0 || MsiViewFetch(view, out record) != 0)
                    return null;
                uint size = 39;
                var productCode = new StringBuilder((int)size);
                if (MsiRecordGetString(record, 1, productCode, ref size) != 0 ||
                    !Guid.TryParseExact(productCode.ToString(), "B", out var id))
                    return null;

                // MSI's current-user/machine installed-product registration for
                // this exact package, not an unrelated product with a similar name.
                size = 32768;
                string code = id.ToString("B").ToUpperInvariant();
                var installLocation = new StringBuilder((int)size);
                return MsiGetProductInfo(code, "InstallLocation", installLocation, ref size) == 0 && size != 0
                    ? installLocation.ToString() : null;
            }
            finally
            {
                if (record != 0) MsiCloseHandle(record);
                if (view != 0) MsiCloseHandle(view);
                if (database != 0) MsiCloseHandle(database);
            }
        }

        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiOpenDatabase(string path, IntPtr persist, out uint database);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiDatabaseOpenView(uint database, string query, out uint view);
        [DllImport("msi.dll")]
        private static extern uint MsiViewExecute(uint view, uint record);
        [DllImport("msi.dll")]
        private static extern uint MsiViewFetch(uint view, out uint record);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiRecordGetString(uint record, uint field, StringBuilder value, ref uint size);
        [DllImport("msi.dll", CharSet = CharSet.Unicode)]
        private static extern uint MsiGetProductInfo(string product, string property, StringBuilder value, ref uint size);
        [DllImport("msi.dll")]
        private static extern uint MsiCloseHandle(uint handle);
    }

    internal sealed class UpdateOptions
    {
        public string Mode { get; private set; }
        public string PackagePath { get; private set; }
        public string TargetDirectory { get; private set; }
        public string LaunchExecutable { get; private set; }
        public int WaitProcessId { get; private set; }
        public bool FromTemp { get; private set; }
        public string ReadyPath { get; private set; }
        public string CultureName { get; private set; }

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
                ReadyPath = Get(values, "ready"),
                CultureName = Get(values, "culture"),
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
