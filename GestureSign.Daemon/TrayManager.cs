using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.UI;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Properties;
using Microsoft.Win32;

namespace GestureSign.Daemon
{
    public class TrayManager : ILoadable, ITrayManager
    {
        #region Private Variables

        static readonly TrayManager _Instance = new TrayManager();

        #endregion

        #region Controls Initialization

        private NotifyIcon _trayIcon;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _disableGesturesMenuItem;
        private ToolStripMenuItem _controlPanelMenuItem;
        private ToolStripMenuItem _exitGestureSignMenuItem;
        private Icon _currentTrayIcon;
        private static DateTime _lastControlPanelStartUtc = DateTime.MinValue;
        private static readonly object _controlPanelStartLock = new object();

        #endregion

        #region Private Methods

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private void SetupTrayIconAndTrayMenu()
        {
            _trayIcon = new NotifyIcon();
            _trayMenu = new ContextMenuStrip();
            _disableGesturesMenuItem = new ToolStripMenuItem();
            _controlPanelMenuItem = new ToolStripMenuItem();
            _exitGestureSignMenuItem = new ToolStripMenuItem();

            _trayIcon.ContextMenuStrip = _trayMenu;
            _trayIcon.Text = "GestureSign";
            _trayIcon.DoubleClick += (o, e) => { TrayIcon_Click(o, (MouseEventArgs)e); };
            _trayIcon.Click += (o, e) => { TrayIcon_Click(o, (MouseEventArgs)e); };
            SetTrayIcon(TrayIconState.Normal);

            _trayMenu.Items.AddRange(new ToolStripItem[] { _disableGesturesMenuItem, new ToolStripSeparator(), _controlPanelMenuItem, new ToolStripSeparator(), _exitGestureSignMenuItem });
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

            _controlPanelMenuItem.Name = "ControlPanel";
            _controlPanelMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.ControlPanel");
            _controlPanelMenuItem.Click += (o, e) =>
            {
                StartControlPanel();
            };

            _exitGestureSignMenuItem.Name = "ExitGestureSign";
            _exitGestureSignMenuItem.Text = LocalizationProvider.Instance.GetTextValue("TrayMenu.Exit");
            _exitGestureSignMenuItem.Click += async (o, e) =>
            {
                await NamedPipe.SendMessageAsync(IpcCommands.Exit, Constants.ControlPanel, wait: false);
                Application.DoEvents();
                Application.Exit();
            };

            ApplyTrayMenuTheme();
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
            _currentTrayIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (_currentTrayIcon == null)
                _currentTrayIcon = (Icon)Resources.normal_daemon.Clone();
            _trayIcon.Icon = _currentTrayIcon;

            if (oldIcon != null)
                oldIcon.Dispose();
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

        private void TrayIcon_Click(object sender, MouseEventArgs e)
        {
            switch (e.Button)
            {
                case MouseButtons.Left:
                    if (e.Clicks == 2 && PointCapture.Instance.Mode != CaptureMode.Training)
                        StartControlPanel();
                    break;
                case MouseButtons.Right:
                    break;
                case MouseButtons.Middle:
                    ToggleDisableGestures();
                    break;
            }
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

            if (_trayIcon.Icon == null)
                SetTrayIcon(TrayIconState.Normal);

            _trayIcon.Visible = AppConfig.ShowTrayIcon;
        }

        public static void StartControlPanel()
        {
            lock (_controlPanelStartLock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - _lastControlPanelStartUtc).TotalMilliseconds < 1200)
                    return;
                _lastControlPanelStartUtc = now;
            }

            string path = FindControlPanelPath();
            if (File.Exists(path))
            {
                using (Process controlPanel = new Process())
                {
                    try
                    {
                        controlPanel.StartInfo.FileName = path;
                        controlPanel.StartInfo.WorkingDirectory = Path.GetDirectoryName(path);
                        controlPanel.Start();
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

        private static string FindControlPanelPath()
        {
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates =
            {
                Path.Combine(baseDirectory, "GestureSign.WinUI.exe"),
                Path.Combine(baseDirectory, "GestureSign-WinUI-Preview", "GestureSign.WinUI.exe"),
                Path.GetFullPath(Path.Combine(baseDirectory, "..", "publish", "GestureSign-WinUI-Preview", "GestureSign.WinUI.exe")),
                Path.Combine(baseDirectory, Constants.ControlPanelFileName)
            };

            return candidates.FirstOrDefault(File.Exists) ?? candidates.Last();
        }

        #endregion

        #region Events

        void Application_ApplicationExit(object sender, EventArgs e)
        {
            if (_trayIcon != null) _trayIcon.Visible = false;
            if (_currentTrayIcon != null) _currentTrayIcon.Dispose();
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
                SetTrayIcon(TrayIconState.Disabled);
            }
            else
            {
                _disableGesturesMenuItem.Checked = false;
                SetTrayIcon(e.Mode == CaptureMode.Training ? TrayIconState.Training : TrayIconState.Normal);
            }
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
