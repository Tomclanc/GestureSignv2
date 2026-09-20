using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GestureSign.CorePlugins.ScreenBrightness
{
    // Owned exclusively by the daemon UI thread through PointInfo.Invoke.
    internal sealed class BrightnessOverlay : Form
    {
        private static BrightnessOverlay _current;
        private readonly Timer _hideTimer;
        private int _brightness;

        private BrightnessOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            Opacity = 0.98; // Layered + TRANSPARENT also passes clicks to other processes.
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            BackColor = SystemInformation.HighContrast ? SystemColors.Window : Color.FromArgb(38, 38, 38);
            ForeColor = SystemInformation.HighContrast ? SystemColors.WindowText : Color.White;
            AccessibleName = "Brightness";
            _hideTimer = new Timer { Interval = 1800 };
            _hideTimer.Tick += (_, _) => Close();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var parameters = base.CreateParams;
                // NOACTIVATE, TOOLWINDOW, TRANSPARENT: never capture focus or input.
                parameters.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020;
                return parameters;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        internal static void ShowBrightness(int brightness)
        {
            var overlay = _current;
            if (overlay == null || overlay.IsDisposed)
                _current = overlay = new BrightnessOverlay();
            overlay._brightness = Math.Clamp(brightness, 0, 100);
            overlay.AccessibleDescription = overlay._brightness + "%";
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            // Move to the selected monitor before reading its DPI.
            if (!overlay.Visible) overlay.Location = area.Location;
            _ = overlay.Handle;
            var scale = overlay.DeviceDpi / 96f;
            var width = Math.Min((int)(300 * scale), area.Width);
            var height = Math.Min((int)(64 * scale), area.Height);
            overlay.SetBounds(area.Left + (area.Width - width) / 2,
                Math.Max(area.Top, area.Bottom - height - (int)(24 * scale)), width, height);
            overlay._hideTimer.Stop();
            if (!overlay.Visible) overlay.Show();
            overlay.Invalidate();
            overlay._hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var scale = ClientSize.Width / 300f;
            g.ScaleTransform(scale, scale);
            using var foreground = new SolidBrush(ForeColor);
            using var track = new Pen(SystemInformation.HighContrast ? SystemColors.GrayText : Color.FromArgb(90, 90, 90), 5);
            using var fill = new Pen(SystemInformation.HighContrast ? SystemColors.Highlight : Color.FromArgb(96, 205, 255), 5);
            using var sun = new Pen(ForeColor, 1.6f);
            g.DrawEllipse(sun, 25, 25, 14, 14);
            for (int ray = 0; ray < 8; ray++)
            {
                var angle = ray * Math.PI / 4;
                g.DrawLine(sun, 32 + (float)Math.Cos(angle) * 10, 32 + (float)Math.Sin(angle) * 10,
                    32 + (float)Math.Cos(angle) * 14, 32 + (float)Math.Sin(angle) * 14);
            }
            track.StartCap = track.EndCap = fill.StartCap = fill.EndCap = LineCap.Round;
            g.DrawLine(track, 65, 32, 226, 32);
            if (_brightness > 0) g.DrawLine(fill, 65, 32, 65 + 161 * _brightness / 100f, 32);
            using var font = new Font("Segoe UI", 15, FontStyle.Regular, GraphicsUnit.Pixel);
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(_brightness + "%", font, foreground, new RectangleF(237, 0, 59, 64), format);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hideTimer.Dispose();
                if (ReferenceEquals(_current, this)) _current = null;
            }
            base.Dispose(disposing);
        }
    }
}
