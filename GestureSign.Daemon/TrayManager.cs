using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.UI;
using GestureSign.Daemon.Input;
using Microsoft.Win32;

namespace GestureSign.Daemon
{
    public class TrayManager : ILoadable, ITrayManager
    {
        private const string TrayTooltipText = "GestureSign V2";

        #region Private Variables

        static readonly TrayManager _Instance = new TrayManager();

        #endregion

        #region Controls Initialization

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _disableGesturesMenuItem;
        private ToolStripMenuItem _settingsMenuItem;
        private ToolStripMenuItem _exitGestureSignMenuItem;
        private Icon _currentTrayIcon;
        private TouchFriendlyTrayMenu _touchTrayMenu;
        private string _loadedCultureName;
        private static DateTime _lastSettingsStartUtc = DateTime.MinValue;
        private static readonly object _settingsStartLock = new object();

        #endregion

        #region Private Methods

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private void SetupTrayIconAndTrayMenu()
        {
            _trayIcon = new NotifyIcon();
            _trayMenu = new ContextMenuStrip();
            _disableGesturesMenuItem = new ToolStripMenuItem();
            _settingsMenuItem = new ToolStripMenuItem();
            _exitGestureSignMenuItem = new ToolStripMenuItem();

            _trayIcon.ContextMenuStrip = null;
            _trayIcon.Text = TrayTooltipText;
            _trayIcon.DoubleClick += (o, e) => { TrayIcon_Click(o, (MouseEventArgs)e); };
            _trayIcon.Click += (o, e) => { TrayIcon_Click(o, (MouseEventArgs)e); };
            _trayIcon.MouseUp += TrayIcon_MouseUp;
            SetTrayIcon(TrayIconState.Normal);

            _trayMenu.Items.AddRange(new ToolStripItem[] { _disableGesturesMenuItem, new ToolStripSeparator(), _settingsMenuItem, new ToolStripSeparator(), _exitGestureSignMenuItem });
            _trayMenu.Name = "TrayMenu";
            _trayMenu.ShowCheckMargin = false;
            _trayMenu.ShowImageMargin = false;
            _trayMenu.Padding = new Padding(8);
            _trayMenu.Opened += (o, e) => ApplyRoundedRegion();
            _trayMenu.SizeChanged += (o, e) => ApplyRoundedRegion();

            _disableGesturesMenuItem.Checked = false;
            _disableGesturesMenuItem.Name = "DisableGesturesMenuItem";
            _disableGesturesMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Disable");
            _disableGesturesMenuItem.Click += (o, e) => { ToggleDisableGestures(); };

            _settingsMenuItem.Name = "Settings";
            _settingsMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Settings");
            _settingsMenuItem.Click += (o, e) =>
            {
                StartSettings();
            };

            _exitGestureSignMenuItem.Name = "ExitGestureSign";
            _exitGestureSignMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit");
            _exitGestureSignMenuItem.Click += async (o, e) =>
            {
                await ExitGestureSignAsync();
            };

            ApplyTrayMenuTheme();
            RebuildTouchTrayMenu();
        }

        private enum TrayIconState
        {
            Normal,
            Disabled,
            Training
        }

        private void SetTrayIcon(TrayIconState state)
        {
            Icon oldIcon = _currentTrayIcon;
            switch (state)
            {
                case TrayIconState.Disabled:
                    _currentTrayIcon = CreateStatusIcon(Color.FromArgb(220, 38, 38), true);
                    _trayIcon.Text = TrayTooltipText;
                    break;
                case TrayIconState.Training:
                    _currentTrayIcon = CreateStatusIcon(Color.FromArgb(37, 99, 235), false);
                    _trayIcon.Text = TrayTooltipText;
                    break;
                default:
                    _currentTrayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                    if (_currentTrayIcon == null)
                        _currentTrayIcon = CreateStatusIcon(Color.FromArgb(0, 120, 212), false);
                    _trayIcon.Text = TrayTooltipText;
                    break;
            }
            _trayIcon.Icon = _currentTrayIcon;

            if (oldIcon != null)
                oldIcon.Dispose();
        }

        private static Icon CreateStatusIcon(Color fillColor, bool drawStopMark)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            using (Brush fill = new SolidBrush(fillColor))
            using (Pen outline = new Pen(Color.FromArgb(245, 255, 255, 255), 3))
            using (Pen mark = new Pen(Color.White, 4))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                graphics.FillEllipse(fill, 3, 3, 26, 26);
                graphics.DrawEllipse(outline, 3, 3, 26, 26);

                if (drawStopMark)
                    graphics.DrawLine(mark, 11, 21, 21, 11);
                else
                    graphics.DrawString("G", SystemFonts.CaptionFont, Brushes.White, 8, 6);

                IntPtr iconHandle = bitmap.GetHicon();
                try
                {
                    using (Icon icon = Icon.FromHandle(iconHandle))
                        return (Icon)icon.Clone();
                }
                finally
                {
                    DestroyIcon(iconHandle);
                }
            }
        }

        private void ApplyRoundedRegion()
        {
            if (_trayMenu == null || _trayMenu.Width <= 0 || _trayMenu.Height <= 0)
                return;

            Region oldRegion = _trayMenu.Region;
            _trayMenu.Region = new Region(CreateRoundedRectanglePath(new Rectangle(Point.Empty, _trayMenu.Size), 8));
            if (oldRegion != null)
                oldRegion.Dispose();
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            Rectangle arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
            GraphicsPath path = new GraphicsPath();

            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter - 1;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter - 1;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();

            return path;
        }

        private void ApplyTrayMenuTheme()
        {
            if (_trayMenu == null)
                return;

            bool lightTheme = IsLightTheme();
            Color backColor = lightTheme ? Color.FromArgb(250, 250, 250) : Color.FromArgb(32, 32, 32);
            Color foreColor = lightTheme ? Color.FromArgb(24, 24, 24) : Color.FromArgb(242, 242, 242);
            Color selectedColor = lightTheme ? Color.FromArgb(232, 240, 254) : Color.FromArgb(63, 63, 63);
            Color borderColor = lightTheme ? Color.FromArgb(210, 210, 210) : Color.FromArgb(64, 64, 64);

            _trayMenu.BackColor = backColor;
            _trayMenu.ForeColor = foreColor;
            _trayMenu.Renderer = new ThemedToolStripRenderer(backColor, foreColor, selectedColor, borderColor);

            foreach (ToolStripItem item in _trayMenu.Items)
            {
                item.BackColor = backColor;
                item.ForeColor = foreColor;
            }

            ApplyTouchTrayMenuTheme();
        }

        private void ApplyTouchTrayMenuTheme()
        {
            if (_touchTrayMenu == null || _touchTrayMenu.IsDisposed)
                return;

            bool lightTheme = IsLightTheme();
            Color backColor = lightTheme ? Color.FromArgb(250, 250, 250) : Color.FromArgb(32, 32, 32);
            Color foreColor = lightTheme ? Color.FromArgb(24, 24, 24) : Color.FromArgb(242, 242, 242);
            Color selectedColor = lightTheme ? Color.FromArgb(232, 240, 254) : Color.FromArgb(63, 63, 63);
            Color borderColor = lightTheme ? Color.FromArgb(210, 210, 210) : Color.FromArgb(64, 64, 64);
            _touchTrayMenu.ApplyTheme(backColor, foreColor, selectedColor, borderColor);
        }

        private static bool IsLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int)
                        return (int)value != 0;
                }
            }
            catch
            {
            }

            return true;
        }

        private sealed class ThemedToolStripRenderer : ToolStripProfessionalRenderer
        {
            private readonly Color _backColor;
            private readonly Color _foreColor;
            private readonly Color _selectedColor;
            private readonly Color _borderColor;

            public ThemedToolStripRenderer(Color backColor, Color foreColor, Color selectedColor, Color borderColor)
            {
                _backColor = backColor;
                _foreColor = foreColor;
                _selectedColor = selectedColor;
                _borderColor = borderColor;
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.Clear(_backColor);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using (Pen pen = new Pen(_borderColor))
                using (GraphicsPath path = CreateRoundedRectanglePath(new Rectangle(Point.Empty, e.ToolStrip.Size), 8))
                    e.Graphics.DrawPath(pen, path);
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                Rectangle bounds = new Rectangle(Point.Empty, e.Item.Size);
                using (Brush brush = new SolidBrush(e.Item.Selected ? _selectedColor : _backColor))
                    e.Graphics.FillRectangle(brush, bounds);
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                int y = e.Item.Height / 2;
                using (Pen pen = new Pen(_borderColor))
                    e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = _foreColor;
                base.OnRenderItemText(e);
            }
        }

        private sealed class TouchFriendlyTrayMenu : Form
        {
            private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
            private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
            private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
            private const int DWMSBT_MAINWINDOW = 2;
            private const int DWMWCP_ROUND = 2;
            private const int CornerRadius = 14;
            private readonly Action _disableGestures;
            private readonly Action _openSettings;
            private readonly Func<Task> _exitGestureSign;
            private readonly Font _menuFont;
            private readonly Rectangle[] _itemBounds = new Rectangle[3];
            private bool _lightTheme = true;
            private Color _normalBackColor;
            private Color _selectedBackColor;
            private Color _borderColor;
            private Color _accentLineColor;
            private Color _foreColor;
            private int _hoverIndex = -1;
            private string _disableText = "";
            private string _controlPanelText = "";
            private string _exitText = "";

            [DllImport("dwmapi.dll")]
            private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

            public TouchFriendlyTrayMenu(Action disableGestures, Action openSettings, Func<Task> exitGestureSign)
            {
                _disableGestures = disableGestures;
                _openSettings = openSettings;
                _exitGestureSign = exitGestureSign;
                _menuFont = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Regular, GraphicsUnit.Point);

                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                AutoScaleMode = AutoScaleMode.Dpi;
                DoubleBuffered = true;
                Padding = new Padding(14);
                Width = 320;
                Height = 194;
                BackColor = Color.FromArgb(245, 248, 252);
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Deactivate += (o, e) => Hide();
            }

            public void UpdateItems(string disableText, string controlPanelText, string exitText)
            {
                _disableText = disableText;
                _controlPanelText = controlPanelText;
                _exitText = exitText;
                Invalidate();
            }

            public void ApplyTheme(Color backColor, Color foreColor, Color selectedColor, Color borderColor)
            {
                _lightTheme = IsLightTheme();
                _normalBackColor = _lightTheme
                    ? Color.FromArgb(112, 247, 250, 255)
                    : Color.FromArgb(150, 31, 35, 43);
                _selectedBackColor = _lightTheme
                    ? Color.FromArgb(184, 255, 255, 255)
                    : Color.FromArgb(176, 58, 64, 76);
                _borderColor = _lightTheme
                    ? Color.FromArgb(140, 126, 142, 170)
                    : Color.FromArgb(150, 91, 101, 120);
                _accentLineColor = _lightTheme
                    ? Color.FromArgb(96, 120, 137, 166)
                    : Color.FromArgb(118, 118, 130, 150);
                _foreColor = _lightTheme
                    ? Color.FromArgb(24, 24, 24)
                    : Color.FromArgb(246, 246, 246);
                BackColor = _lightTheme
                    ? Color.FromArgb(245, 248, 252)
                    : Color.FromArgb(32, 35, 42);

                ApplyBackdrop();
                Invalidate();
            }

            public void ShowNearCursor()
            {
                var screen = Screen.FromPoint(Cursor.Position).WorkingArea;
                var x = Math.Min(Cursor.Position.X, screen.Right - Width);
                var y = Math.Min(Cursor.Position.Y, screen.Bottom - Height);
                x = Math.Max(screen.Left, x);
                y = Math.Max(screen.Top, y);
                Location = new Point(x, y);

                if (!Visible)
                    Show();
                Activate();
                BringToFront();
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    const int CS_DROPSHADOW = 0x00020000;
                    var createParams = base.CreateParams;
                    createParams.ClassStyle |= CS_DROPSHADOW;
                    return createParams;
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e)
            {
                base.OnPaintBackground(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SystemDefault;

                var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = CreateRoundedRectanglePath(bounds, CornerRadius))
                using (Brush fill = new SolidBrush(_normalBackColor))
                using (Pen pen = new Pen(_borderColor))
                using (Pen highlight = new Pen(Color.FromArgb(_lightTheme ? 130 : 55, Color.White)))
                {
                    e.Graphics.FillPath(fill, path);
                    e.Graphics.DrawPath(highlight, path);
                    e.Graphics.DrawPath(pen, path);
                }

                UpdateItemBounds();

                for (var index = 0; index < _itemBounds.Length; index++)
                {
                    if (index == _hoverIndex)
                    {
                        using (GraphicsPath hoverPath = CreateRoundedRectanglePath(_itemBounds[index], 8))
                        using (Brush hoverBrush = new SolidBrush(_selectedBackColor))
                            e.Graphics.FillPath(hoverBrush, hoverPath);
                    }
                }

                using (Pen separator = new Pen(Color.FromArgb(_lightTheme ? 100 : 130, _accentLineColor)))
                {
                    var left = Padding.Left + 8;
                    var right = Width - Padding.Right - 8;
                    e.Graphics.DrawLine(separator, left, _itemBounds[0].Bottom, right, _itemBounds[0].Bottom);
                    e.Graphics.DrawLine(separator, left, _itemBounds[1].Bottom, right, _itemBounds[1].Bottom);
                }

                DrawMenuText(e.Graphics, _disableText, _itemBounds[0]);
                DrawMenuText(e.Graphics, _controlPanelText, _itemBounds[1]);
                DrawMenuText(e.Graphics, _exitText, _itemBounds[2]);
            }

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                ApplyBackdrop();
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Region oldRegion = Region;
                Region = new Region(CreateRoundedRectanglePath(new Rectangle(Point.Empty, Size), CornerRadius));
                if (oldRegion != null)
                    oldRegion.Dispose();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                var hoverIndex = HitTest(e.Location);
                if (hoverIndex == _hoverIndex)
                    return;

                _hoverIndex = hoverIndex;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (_hoverIndex == -1)
                    return;

                _hoverIndex = -1;
                Invalidate();
            }

            protected override async void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (e.Button != MouseButtons.Left)
                    return;

                var itemIndex = HitTest(e.Location);
                if (itemIndex < 0)
                    return;

                Hide();
                switch (itemIndex)
                {
                    case 0:
                        _disableGestures();
                        break;
                    case 1:
                        _openSettings();
                        break;
                    case 2:
                        await _exitGestureSign();
                        break;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    _menuFont.Dispose();
                base.Dispose(disposing);
            }

            private void UpdateItemBounds()
            {
                const int itemHeight = 54;
                var x = Padding.Left + 8;
                var width = Width - Padding.Left - Padding.Right - 16;
                var y = Padding.Top + 2;

                for (var index = 0; index < _itemBounds.Length; index++)
                {
                    _itemBounds[index] = new Rectangle(x, y, width, itemHeight);
                    y += itemHeight;
                }
            }

            private int HitTest(Point point)
            {
                UpdateItemBounds();
                for (var index = 0; index < _itemBounds.Length; index++)
                {
                    if (_itemBounds[index].Contains(point))
                        return index;
                }

                return -1;
            }

            private void DrawMenuText(Graphics graphics, string text, Rectangle bounds)
            {
                var textBounds = new Rectangle(bounds.Left + 18, bounds.Top, bounds.Width - 36, bounds.Height);
                TextRenderer.DrawText(
                    graphics,
                    text,
                    _menuFont,
                    textBounds,
                    _foreColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            private void ApplyBackdrop()
            {
                if (!IsHandleCreated || Environment.OSVersion.Version.Build < 22000)
                    return;

                try
                {
                    int darkMode = _lightTheme ? 0 : 1;
                    DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                    int cornerPreference = DWMWCP_ROUND;
                    DwmSetWindowAttribute(Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPreference, sizeof(int));

                    int backdrop = DWMSBT_MAINWINDOW;
                    DwmSetWindowAttribute(Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
                }
                catch
                {
                }
            }
        }

        private void TrayIcon_Click(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    if (e.Clicks == 2 && PointCapture.Instance.Mode != CaptureMode.Training)
                        StartSettings();
                    else if (e.Clicks == 1)
                        ShowTouchTrayMenu();
                    break;
                case MouseButtons.Right:
                    break;
                case MouseButtons.Middle:
                    ToggleDisableGestures();
                    break;
            }
        }

        private void TrayIcon_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
                ShowTouchTrayMenu();
        }

        private void ShowTouchTrayMenu()
        {
            if (_touchTrayMenu == null || _touchTrayMenu.IsDisposed)
                RebuildTouchTrayMenu();

            if (_touchTrayMenu == null)
                return;

            _touchTrayMenu.UpdateItems(
                _disableGesturesMenuItem.Text,
                LocalizationProvider.Instance.GetTextValue("TrayMenu.Settings"),
                LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit"));
            _touchTrayMenu.ShowNearCursor();
        }

        private void RebuildTouchTrayMenu()
        {
            if (_touchTrayMenu != null && !_touchTrayMenu.IsDisposed)
                _touchTrayMenu.Dispose();

            _touchTrayMenu = new TouchFriendlyTrayMenu(
                () =>
                {
                    ToggleDisableGestures();
                    _touchTrayMenu.UpdateItems(
                        _disableGesturesMenuItem.Text,
                        LocalizationProvider.Instance.GetTextValue("TrayMenu.Settings"),
                        LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit"));
                },
                StartSettings,
                async () => await ExitGestureSignAsync());
            ApplyTouchTrayMenuTheme();
        }

        #endregion

        #region Constructors

        protected TrayManager()
        {
            PointCapture.Instance.ModeChanged += CaptureMode_Changed;
            Application.ApplicationExit += Application_ApplicationExit;
            SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;
        }

        #endregion

        #region Public Properties

        public static TrayManager Instance
        {
            get { return _Instance; }
        }

        public bool TrayIconVisible
        {
            get { return _trayIcon.Visible; }
            set { _trayIcon.Visible = value; }
        }

        public void Load()
        {
            SetupTrayIconAndTrayMenu();
            ApplyConfiguration();

            AppConfig.ConfigChanged += (o, ea) =>
            {
                ApplyConfiguration();
            };
        }

        public void ApplyConfiguration()
        {
            if (_trayIcon == null)
                return;

            ReloadLocalizationIfNeeded();

            if (_trayIcon.Icon == null)
                SetTrayIcon(TrayIconState.Normal);

            _trayIcon.Visible = AppConfig.ShowTrayIcon;
        }

        private void ReloadLocalizationIfNeeded()
        {
            string cultureName = String.IsNullOrEmpty(AppConfig.CultureName)
                ? System.Globalization.CultureInfo.CurrentUICulture.Name
                : AppConfig.CultureName;
            if (String.Equals(_loadedCultureName, cultureName, StringComparison.OrdinalIgnoreCase))
                return;

            _loadedCultureName = cultureName;
            LocalizationProvider.Instance.ReloadCulture();
            if (!LocalizationProvider.Instance.LoadFromFile("Daemon"))
                LocalizationProvider.Instance.LoadFromResource(Properties.Resources.en);

            bool recognitionDisabled = PointCapture.Instance.Mode == CaptureMode.UserDisabled;
            _disableGesturesMenuItem.Text = LocalizationProvider.Instance.GetTextValue(
                recognitionDisabled ? "TrayMenu.Enable" : "TrayMenu.Disable");
            _settingsMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Settings");
            _exitGestureSignMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit");

            if (_touchTrayMenu != null && !_touchTrayMenu.IsDisposed)
                _touchTrayMenu.UpdateItems(
                    _disableGesturesMenuItem.Text,
                    _settingsMenuItem.Text,
                    _exitGestureSignMenuItem.Text);
        }

        public static void StartSettings()
        {
            lock (_settingsStartLock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastSettingsStartUtc).TotalMilliseconds < 1200)
                    return;
                _lastSettingsStartUtc = now;
            }

            string path = FindSettingsPath();
            if (File.Exists(path))
            {
                using (Process settings = new Process())
                {
                    try
                    {
                        settings.StartInfo.FileName = path;
                        settings.StartInfo.WorkingDirectory = Path.GetDirectoryName(path);
                        settings.Start();
                    }
                    catch (Exception exception)
                    {
                        Logging.LogException(exception);
                        MessageBox.Show(exception.ToString(),
                            LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButtons.OK,
                            MessageBoxIcon.Warning);
                    }
                }
            }
            else
            {
                MessageBox.Show(string.Format(LocalizationProvider.Instance.GetTextValue("Messages.ComponentNotFoundMessage"), path),
                    LocalizationProvider.Instance.GetTextValue("Messages.Error"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static string FindSettingsPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "GestureSign.WinUI.exe")),
                Path.Combine(baseDirectory, "GestureSign.WinUI.exe"),
                Path.Combine(baseDirectory, "GestureSign-WinUI-Preview", "GestureSign.WinUI.exe"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "publish", "GestureSign-WinUI-Preview", "GestureSign.WinUI.exe"))
            };

            return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        }

        internal static async Task ExitGestureSignAsync()
        {
            Logging.LogMessage("ExitGestureSignAsync started.");
            KandoLauncher.Stop();

            try
            {
                await NamedPipe.SendMessageAsync(IpcCommands.Exit, Constants.Settings, wait: false);
            }
            catch (Exception exception)
            {
                Logging.LogException(exception);
            }

            await Task.Delay(500);
            CloseOtherGestureSignProcesses();
            Application.DoEvents();
            Logging.LogMessage("ExitGestureSignAsync calling Application.Exit.");
            Application.Exit();
        }

        private static void CloseOtherGestureSignProcesses()
        {
            int currentProcessId = Process.GetCurrentProcess().Id;

            foreach (Process process in Process.GetProcesses())
            {
                using (process)
                {
                    try
                    {
                        if (process.Id == currentProcessId || !IsGestureSignProcess(process))
                            continue;

                        if (process.CloseMainWindow())
                            process.WaitForExit(1500);

                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(3000);
                        }
                    }
                    catch (Exception exception)
                    {
                        Logging.LogException(exception);
                    }
                }
            }
        }

        private static bool IsGestureSignProcess(Process process)
        {
            string processName = process.ProcessName ?? string.Empty;
            if (IsGestureSignProcessName(processName))
                return true;

            try
            {
                string modulePath = process.MainModule == null ? string.Empty : process.MainModule.FileName;
                string fileName = Path.GetFileName(modulePath);
                if (IsGestureSignProcessName(fileName))
                    return true;

                string baseDirectory = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string moduleDirectory = Path.GetDirectoryName(modulePath);
                return !string.IsNullOrEmpty(moduleDirectory)
                       && string.Equals(
                           moduleDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                           baseDirectory,
                           StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGestureSignProcessName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(name);
            return fileNameWithoutExtension.StartsWith(Constants.ProductName, StringComparison.OrdinalIgnoreCase)
                   || fileNameWithoutExtension.StartsWith("GestureSign2", StringComparison.OrdinalIgnoreCase);
        }

        #endregion

        #region Events

        void Application_ApplicationExit(object sender, EventArgs e)
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            if (_currentTrayIcon != null) _currentTrayIcon.Dispose();
            if (_touchTrayMenu != null && !_touchTrayMenu.IsDisposed) _touchTrayMenu.Dispose();
            SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;
        }

        private void SystemEvents_UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General)
                ApplyTrayMenuTheme();
        }

        protected void CaptureMode_Changed(object sender, ModeChangedEventArgs e)
        {
            if (e.Mode == CaptureMode.UserDisabled)
            {
                _disableGesturesMenuItem.Checked = true;
                _disableGesturesMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Enable");
                SetTrayIcon(TrayIconState.Disabled);
            }
            else
            {
                _disableGesturesMenuItem.Checked = false;
                _disableGesturesMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Disable");
                SetTrayIcon(e.Mode == CaptureMode.Training ? TrayIconState.Training : TrayIconState.Normal);
            }

            if (_touchTrayMenu != null && !_touchTrayMenu.IsDisposed)
                _touchTrayMenu.UpdateItems(
                    _disableGesturesMenuItem.Text,
                    LocalizationProvider.Instance.GetTextValue("TrayMenu.Settings"),
                    LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit"));
        }

        #endregion

        #region Public Methods

        public void ToggleDisableGestures()
        {
            PointCapture.Instance.ToggleUserDisablePointCapture();
        }

        #endregion
    }
}
