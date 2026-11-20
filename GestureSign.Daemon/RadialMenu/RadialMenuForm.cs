using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using GestureSign.Daemon.Surface;
using ManagedWinapi.Windows;

namespace GestureSign.Daemon.RadialMenu
{
    /// <summary>
    /// Click-through, non-activating layered window that draws the radial menu.
    /// It never takes focus, so the target window keeps its selection while
    /// the menu is visible.
    /// </summary>
    internal sealed class RadialMenuForm : Form
    {
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;

        private DiBitmap _bitmap;
        private Rectangle _bounds;

        public RadialMenuForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            CreateHandle();
        }

        protected override bool ShowWithoutActivation => true;

        protected override CreateParams CreateParams
        {
            get
            {
                var p = base.CreateParams;
                p.ExStyle = (int)WindowExStyleFlags.NOACTIVATE |
                            (int)WindowExStyleFlags.TOOLWINDOW |
                            (int)WindowExStyleFlags.TRANSPARENT |
                            (int)WindowExStyleFlags.TOPMOST |
                            (int)WindowExStyleFlags.LAYERED;
                return p;
            }
        }

        public void Render(RadialMenuLayout layout, Point cursor, RadialMenuHit selected)
        {
            try
            {
                var size = layout.WindowSize;
                if (_bitmap == null || _bitmap.Size.Width != size || _bitmap.Size.Height != size)
                {
                    _bitmap?.Dispose();
                    _bitmap = new DiBitmap(new Size(size, size));
                }
                _bounds = new Rectangle(layout.Center.X - size / 2, layout.Center.Y - size / 2, size, size);

                var g = _bitmap.BeginDraw();
                try
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    Draw(g, layout, new Point(cursor.X - _bounds.X, cursor.Y - _bounds.Y), selected);
                }
                finally
                {
                    _bitmap.EndDraw();
                }

                if (!Visible)
                {
                    // Form.Show applies the cached WinForms bounds; set them first
                    // and push the layered content again afterwards.
                    Bounds = _bounds;
                    Push();
                    Show();
                    NativeMethods.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0,
                        NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE |
                        NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_SHOWWINDOW);
                }
                Push();
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }

        public void HideMenu()
        {
            if (Visible)
                Hide();
        }

        private static void Draw(Graphics g, RadialMenuLayout layout, Point cursor, RadialMenuHit selected)
        {
            var dark = RadialMenuLayout.IsDarkTheme();
            var s = layout.Scale;
            var c = new PointF(layout.WindowSize / 2f, layout.WindowSize / 2f);
            var outerRadius = layout.HasOuter ? layout.OuterRadius : layout.InnerRadius;

            var background = dark ? Color.FromArgb(236, 44, 44, 44) : Color.FromArgb(240, 250, 250, 250);
            var center = dark ? Color.FromArgb(255, 56, 56, 56) : Color.FromArgb(255, 238, 238, 238);
            var line = dark ? Color.FromArgb(90, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0);
            var highlight = dark ? Color.FromArgb(255, 30, 70, 120) : Color.FromArgb(255, 214, 230, 250);
            var text = dark ? Color.FromArgb(255, 235, 235, 235) : Color.FromArgb(255, 30, 30, 30);
            var accentText = dark ? Color.FromArgb(255, 160, 200, 255) : Color.FromArgb(255, 0, 90, 180);
            var pointer = Color.FromArgb(255, 226, 75, 74);

            using (var b = new SolidBrush(background))
                g.FillEllipse(b, Circle(c, outerRadius));
            using (var p = new Pen(line, Math.Max(1f, s)))
                g.DrawEllipse(p, Circle(c, outerRadius));

            if (selected != null)
            {
                var r1 = selected.Outer ? layout.InnerRadius : layout.DeadZoneRadius;
                var r2 = selected.Outer ? layout.OuterRadius : layout.InnerRadius;
                using (var path = Sector(c, r1, r2, selected.Index))
                using (var b = new SolidBrush(highlight))
                    g.FillPath(b, path);
            }

            using (var p = new Pen(line, Math.Max(1f, s)))
            {
                for (var i = 0; i < RadialMenuLayout.Slots; i++)
                {
                    var a = RadialMenuLayout.SlotAngle(i) + Math.PI / RadialMenuLayout.Slots;
                    g.DrawLine(p, Polar(c, layout.DeadZoneRadius, a), Polar(c, outerRadius, a));
                }
                if (layout.HasOuter)
                    g.DrawEllipse(p, Circle(c, layout.InnerRadius));
            }

            using (var b = new SolidBrush(center))
                g.FillEllipse(b, Circle(c, layout.DeadZoneRadius));
            using (var p = new Pen(line, Math.Max(1f, s)))
                g.DrawEllipse(p, Circle(c, layout.DeadZoneRadius));

            using (var font = new Font("Microsoft YaHei UI", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font("Microsoft YaHei UI", 12f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(text))
            using (var accentBrush = new SolidBrush(accentText))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter
            })
            {
                foreach (var slot in layout.Labels)
                {
                    var r1 = slot.Outer ? layout.InnerRadius : layout.DeadZoneRadius;
                    var r2 = slot.Outer ? layout.OuterRadius : layout.InnerRadius;
                    var mid = Polar(c, (r1 + r2) / 2f, RadialMenuLayout.SlotAngle(slot.Index));
                    var width = (slot.Outer ? 84f : 72f) * s;
                    var height = 40f * s;
                    var rect = new RectangleF(mid.X - width / 2f, mid.Y - height / 2f, width, height);
                    var isSelected = selected != null && selected.Outer == slot.Outer && selected.Index == slot.Index;
                    g.DrawString(slot.Text, isSelected ? bold : font, isSelected ? accentBrush : textBrush, rect, format);
                }
            }

            var dx = cursor.X - c.X;
            var dy = cursor.Y - c.Y;
            var distance = (float)Math.Sqrt(dx * dx + dy * dy);
            if (distance > 1f)
            {
                var k = distance > outerRadius ? outerRadius / distance : 1f;
                using (var p = new Pen(pointer, 3f * s) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLine(p, c, new PointF(c.X + dx * k, c.Y + dy * k));
            }
        }

        private static RectangleF Circle(PointF c, float r) => new RectangleF(c.X - r, c.Y - r, r * 2, r * 2);

        private static PointF Polar(PointF c, float r, double angle)
            => new PointF(c.X + r * (float)Math.Sin(angle), c.Y - r * (float)Math.Cos(angle));

        private static GraphicsPath Sector(PointF c, float r1, float r2, int index)
        {
            // GDI+ angles start at 3 o'clock and grow clockwise; slot 0 is 12 o'clock.
            var sweep = 360f / RadialMenuLayout.Slots;
            var start = -90f + index * sweep - sweep / 2f;
            var path = new GraphicsPath();
            path.AddArc(Circle(c, r2), start, sweep);
            path.AddArc(Circle(c, r1), start + sweep, -sweep);
            path.CloseFigure();
            return path;
        }

        private void Push()
        {
            var memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            var old = IntPtr.Zero;
            try
            {
                old = NativeMethods.SelectObject(memDc, _bitmap.HBitmap);
                var winSize = new NativeMethods.Size(_bounds.Width, _bounds.Height);
                var winPos = new NativeMethods.Point(_bounds.X, _bounds.Y);
                var src = new NativeMethods.Point(0, 0);
                var blend = new NativeMethods.BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = 255, AlphaFormat = AC_SRC_ALPHA };
                var info = new NativeMethods.UPDATELAYEREDWINDOWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.UPDATELAYEREDWINDOWINFO)),
                    dwFlags = ULW_ALPHA,
                    hdcDst = IntPtr.Zero,
                    hdcSrc = memDc
                };
                unsafe
                {
                    info.pblend = &blend;
                    info.pptDst = &winPos;
                    info.psize = &winSize;
                    info.pptSrc = &src;
                    info.prcDirty = null;
                }
                NativeMethods.UpdateLayeredWindowIndirect(Handle, ref info);
            }
            finally
            {
                if (old != IntPtr.Zero)
                    NativeMethods.SelectObject(memDc, old);
                NativeMethods.DeleteDC(memDc);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _bitmap?.Dispose();
                _bitmap = null;
            }
            base.Dispose(disposing);
        }
    }
}
