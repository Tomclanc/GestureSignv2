using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
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

        // Drawing happens on a premultiplied managed canvas (correct alpha for
        // text and icons) and is then copied into a top-down DIB section for
        // UpdateLayeredWindow.
        private Bitmap _canvas;
        private IntPtr _hBitmap;
        private IntPtr _bits;
        private int _size;
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
                EnsureSurface(size);
                _bounds = new Rectangle(layout.Center.X - size / 2, layout.Center.Y - size / 2, size, size);

                using (var g = Graphics.FromImage(_canvas))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);
                    Draw(g, layout, new Point(cursor.X - _bounds.X, cursor.Y - _bounds.Y), selected);
                }
                CopyCanvasToDib();

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
            var outerRadius = layout.RingOuterRadius(layout.RingCount - 1);

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
                var r1 = layout.RingInnerRadius(selected.Ring);
                var r2 = layout.RingOuterRadius(selected.Ring);
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
                for (var ring = 0; ring < layout.RingCount - 1; ring++)
                    g.DrawEllipse(p, Circle(c, layout.RingOuterRadius(ring)));
            }

            using (var b = new SolidBrush(center))
                g.FillEllipse(b, Circle(c, layout.DeadZoneRadius));
            using (var p = new Pen(line, Math.Max(1f, s)))
                g.DrawEllipse(p, Circle(c, layout.DeadZoneRadius));

            var glyphFontName = RadialMenuIcons.GlyphFontName;
            using (var glyphFont = glyphFontName != null ? new Font(glyphFontName, 20f * s, FontStyle.Regular, GraphicsUnit.Pixel) : null)
            using (var font = new Font("Microsoft YaHei UI", 12f * s, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var bold = new Font("Microsoft YaHei UI", 12f * s, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var textBrush = new SolidBrush(text))
            using (var accentBrush = new SolidBrush(accentText))
            using (var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
                Trimming = StringTrimming.EllipsisCharacter,
                FormatFlags = StringFormatFlags.NoWrap
            })
            {
                foreach (var slot in layout.Labels)
                {
                    if (slot.Ring >= layout.RingCount)
                        continue;
                    var r1 = layout.RingInnerRadius(slot.Ring);
                    var r2 = layout.RingOuterRadius(slot.Ring);
                    var mid = Polar(c, (r1 + r2) / 2f, RadialMenuLayout.SlotAngle(slot.Index));
                    var width = (slot.Ring == 0 ? 72f : slot.Ring == 1 ? 84f : 96f) * s;
                    var isSelected = selected != null && selected.Ring == slot.Ring && selected.Index == slot.Index;
                    var brush = isSelected ? accentBrush : textBrush;
                    var iconSize = 22f * s;
                    var hasIcon = slot.Icon != null || (glyphFont != null && !string.IsNullOrEmpty(slot.Glyph));
                    // Icon above, label below; the pair is centered on the slot.
                    var textHeight = 18f * s;
                    var top = mid.Y - (hasIcon ? (iconSize + 2f * s + textHeight) / 2f : textHeight / 2f);
                    if (slot.Icon != null)
                    {
                        g.DrawImage(slot.Icon, new RectangleF(mid.X - iconSize / 2f, top, iconSize, iconSize));
                    }
                    else if (hasIcon)
                    {
                        g.DrawString(slot.Glyph, glyphFont, brush, new RectangleF(mid.X - iconSize, top - 2f * s, iconSize * 2f, iconSize + 4f * s), format);
                    }
                    var textTop = hasIcon ? top + iconSize + 2f * s : top;
                    g.DrawString(slot.Text, isSelected ? bold : font, brush, new RectangleF(mid.X - width / 2f, textTop, width, textHeight), format);
                }
            }

            // In a submenu the center goes back: show a back arrow there.
            if (layout.IsSubmenu && RadialMenuIcons.GlyphFontName != null)
            {
                using var backFont = new Font(RadialMenuIcons.GlyphFontName, 16f * s, FontStyle.Regular, GraphicsUnit.Pixel);
                using var backBrush = new SolidBrush(text);
                using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                g.DrawString("\uE72B", backFont, backBrush, Circle(c, layout.DeadZoneRadius), centered);
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

        private void EnsureSurface(int size)
        {
            if (_canvas != null && _size == size)
                return;
            ReleaseSurface();
            var info = new NativeMethods.BITMAPINFO
            {
                bmiHeader =
                {
                    biSize = Marshal.SizeOf(typeof(NativeMethods.BITMAPINFOHEADER)),
                    biWidth = size,
                    biHeight = -size, // top-down, same row order as the managed canvas
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = 0
                }
            };
            _hBitmap = NativeMethods.CreateDIBSection(IntPtr.Zero, ref info, 0, out _bits, IntPtr.Zero, 0);
            if (_hBitmap == IntPtr.Zero || _bits == IntPtr.Zero)
                throw new InvalidOperationException("CreateDIBSection failed: " + Marshal.GetLastWin32Error());
            _canvas = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            _size = size;
        }

        private void CopyCanvasToDib()
        {
            var rect = new Rectangle(0, 0, _size, _size);
            var data = _canvas.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                var rowBytes = _size * 4;
                unsafe
                {
                    for (var y = 0; y < _size; y++)
                    {
                        var source = (byte*)data.Scan0 + (long)y * data.Stride;
                        var target = (byte*)_bits + (long)y * rowBytes;
                        Buffer.MemoryCopy(source, target, rowBytes, rowBytes);
                    }
                }
            }
            finally
            {
                _canvas.UnlockBits(data);
            }
        }

        private void ReleaseSurface()
        {
            _canvas?.Dispose();
            _canvas = null;
            if (_hBitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(_hBitmap);
                _hBitmap = IntPtr.Zero;
            }
            _bits = IntPtr.Zero;
            _size = 0;
        }

        private void Push()
        {
            var memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            var old = IntPtr.Zero;
            try
            {
                old = NativeMethods.SelectObject(memDc, _hBitmap);
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
            ReleaseSurface();
            base.Dispose(disposing);
        }
    }
}
