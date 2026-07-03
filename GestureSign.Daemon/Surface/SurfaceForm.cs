using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GestureSign.Common.Configuration;
using GestureSign.Common.Log;
using GestureSign.Daemon.Native;
using ManagedWinapi.Windows;
using Microsoft.Win32;

namespace GestureSign.Daemon.Surface
{
    public class SurfaceForm : Form
    {
        #region Private Variables

        private Pen _drawingPen;
        private Pen _dirtyMarkerPen;
        private float _penWidth;
        int[] _lastStroke;
        Size _screenOffset = default(Size);
        DiBitmap _bitmap;
        private GraphicsPath _graphicsPath = new GraphicsPath();
        private GraphicsPath _dirtyGraphicsPath = new GraphicsPath();
        private Timer _gestureHintTimer;
        private Timer _gestureHintFadeTimer;
        private DateTime _gestureHintFadeStartedUtc;
        private int _gestureHintFadeDurationMs;
        private bool _gestureHintFadingOut;

        private bool _settingsChanged;

        private const Int32 ULW_ALPHA = 0x00000002;

        private const byte AC_SRC_OVER = 0x00;
        private const byte AC_SRC_ALPHA = 0x01;
        private const int MinimumVisiblePenWidth = 7;
        private const int MinimumVisibleAlpha = 192;
        private const int GestureHintFadeInMs = 140;
        private const int GestureHintHoldMs = 1050;
        private const int GestureHintFadeOutMs = 220;
        private const int GestureHintFrameIntervalMs = 16;
        #endregion

        #region Constructors

        public SurfaceForm()
        {
            CreateHandle();
            InitializeForm();
            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            // Respond to system event changes by reinitializing the form
            SystemEvents.DisplaySettingsChanged += AppConfig_ConfigChanged;
            SystemEvents.UserPreferenceChanged += AppConfig_ConfigChanged;
            //this.SetStyle(ControlStyles.DoubleBuffer | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            //this.UpdateStyles();
        }

        #endregion

        #region Dispose

        protected override void Dispose(bool disposing)
        {
            if (!IsDisposed)
            {
                if (disposing)
                {
                    AppConfig.ConfigChanged -= AppConfig_ConfigChanged;
                    SystemEvents.DisplaySettingsChanged -= AppConfig_ConfigChanged;
                    SystemEvents.UserPreferenceChanged -= AppConfig_ConfigChanged;
                    _gestureHintTimer?.Dispose();
                    _gestureHintFadeTimer?.Dispose();
                }

                _penWidth = 0;
                _bitmap?.Dispose();
                _graphicsPath?.Dispose();
                _dirtyGraphicsPath?.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion

        #region Events

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            ResetSurface();
        }

        #endregion

        #region Public Methods

        public new void Load()
        {

        }

        public void StartDrawing(List<Point> startPoints)
        {
            if (_settingsChanged)
            {
                _settingsChanged = false;
                InitializeForm();
            }

            if (_penWidth <= 0)
            {
                InitializePen();
                if (_penWidth <= 0)
                {
                    Logging.LogMessage($"Visual feedback skipped. ConfiguredWidth={AppConfig.VisualFeedbackWidth}");
                    return;
                }
            }

            StopGestureHintTimers();
            ClearSurfaces();
            EnsureDrawingSurface();
            EnsureSurfaceVisible();

            var palette = VisualFeedbackPalette();
            _drawingPen.Color = ApplyVisualFeedbackAlpha(palette != null && palette.Length > 0 ? palette[0] : AppConfig.VisualFeedbackColor);
            _drawingPen.Width = _penWidth * DpiHelper.GetScreenDpi(startPoints.FirstOrDefault()) / 96f;
            Logging.LogMessage($"Visual feedback started. Width={_drawingPen.Width:0.##}, Alpha={_drawingPen.Color.A}, Contacts={startPoints?.Count ?? 0}");
        }

        public void EndDrawing()
        {
            if (_penWidth <= 0 || _lastStroke == null)
                return;
            StopGestureHintTimers();
            HideSurface();

            ClearSurfaces();
        }

        public void DrawPoints(List<List<Point>> points)
        {
            if (_penWidth > 0 && !(points.Count == 1 && points[0].Count == 1))
            {

                if (_bitmap == null || _lastStroke == null)
                {
                    ClearSurfaces();
                    EnsureDrawingSurface();
                }
                DrawSegments(points);
            }
        }

        public void ShowGestureActionHint(List<List<Point>> points, string text)
        {
            if (!AppConfig.ShowGestureActionHint)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => ShowGestureActionHint(points, text)));
                return;
            }

            if (_settingsChanged)
            {
                _settingsChanged = false;
                InitializeForm();
            }

            StopGestureHintTimers();
            ClearSurfaces();
            EnsureDrawingSurface();
            EnsureSurfaceVisible();

            if (_penWidth <= 0)
                InitializePen();

            if (_penWidth > 0 && points != null && points.Any(p => p.Count > 1))
                DrawCompleteGesture(points);

            DrawActionHint(string.IsNullOrWhiteSpace(text) ? "已触发" : text.Trim());
            UpdateFullSurface(0);

            StartGestureHintFade(false);
        }

        #endregion

        #region Private Methods

        private void StopGestureHintTimers()
        {
            _gestureHintTimer?.Stop();
            _gestureHintTimer?.Dispose();
            _gestureHintTimer = null;
            _gestureHintFadeTimer?.Stop();
            _gestureHintFadeTimer?.Dispose();
            _gestureHintFadeTimer = null;
        }

        private void StartGestureHintHold()
        {
            _gestureHintTimer?.Dispose();
            _gestureHintTimer = new Timer { Interval = GestureHintHoldMs };
            _gestureHintTimer.Tick += (timerSender, timerArgs) =>
            {
                _gestureHintTimer.Stop();
                _gestureHintTimer.Dispose();
                _gestureHintTimer = null;
                StartGestureHintFade(true);
            };
            _gestureHintTimer.Start();
        }

        private void StartGestureHintFade(bool fadeOut)
        {
            _gestureHintFadeTimer?.Dispose();
            _gestureHintFadingOut = fadeOut;
            _gestureHintFadeDurationMs = fadeOut ? GestureHintFadeOutMs : GestureHintFadeInMs;
            _gestureHintFadeStartedUtc = DateTime.UtcNow;
            _gestureHintFadeTimer = new Timer { Interval = GestureHintFrameIntervalMs };
            _gestureHintFadeTimer.Tick += GestureHintFadeTimer_Tick;
            _gestureHintFadeTimer.Start();
        }

        private void GestureHintFadeTimer_Tick(object sender, EventArgs e)
        {
            if (_bitmap == null)
            {
                StopGestureHintTimers();
                return;
            }

            var elapsedMs = (DateTime.UtcNow - _gestureHintFadeStartedUtc).TotalMilliseconds;
            var progress = Math.Max(0d, Math.Min(1d, elapsedMs / _gestureHintFadeDurationMs));
            var eased = _gestureHintFadingOut
                ? EaseInOutCubic(progress)
                : EaseOutCubic(progress);
            var opacity = _gestureHintFadingOut
                ? (byte)Math.Round(255 * (1d - eased))
                : (byte)Math.Round(255 * eased);

            UpdateFullSurface(opacity);

            if (progress < 1d)
                return;

            _gestureHintFadeTimer.Stop();
            _gestureHintFadeTimer.Dispose();
            _gestureHintFadeTimer = null;

            if (_gestureHintFadingOut)
            {
                HideSurface();
                ClearSurfaces();
                return;
            }

            StartGestureHintHold();
        }

        private static double EaseOutCubic(double progress)
        {
            var inverse = 1d - progress;
            return 1d - inverse * inverse * inverse;
        }

        private static double EaseInOutCubic(double progress)
        {
            return progress < 0.5d
                ? 4d * progress * progress * progress
                : 1d - Math.Pow(-2d * progress + 2d, 3d) / 2d;
        }

        private void DrawSegments(List<List<Point>> points)
        {
            // Ensure that surface is visible
            EnsureSurfaceVisible();
            if (_lastStroke == null || _lastStroke.Length != points.Count)
            {
                _lastStroke = new int[points.Count];
            }
            try
            {
                _dirtyGraphicsPath.Reset();
                var surfaceGraphics = _bitmap.BeginDraw();
                var translatedPointList = new List<KeyValuePair<int, Point[]>>(_lastStroke.Length);

                for (int i = 0; i < _lastStroke.Length; i++)
                {
                    // Create list of points that are new this draw
                    List<Point> newPoints = new List<Point>();
                    // Get number of points added since last draw including last point of last stroke and add new points to new points list

                    var iDelta = points[i].Count - _lastStroke[i] + 1;

                    newPoints.AddRange(points[i].Skip(points[i].Count - iDelta).Take(iDelta));
                    if (newPoints.Count < 2) continue;

                    var translatedPoints = newPoints.Select(TranslatePoint).ToArray();
                    // Draw new line segments to main drawing surface
                    _graphicsPath.AddLines(translatedPoints);

                    _dirtyGraphicsPath.AddLines(translatedPoints);
                    translatedPointList.Add(new KeyValuePair<int, Point[]>(i, translatedPoints));
                }
                _dirtyGraphicsPath.Widen(_dirtyMarkerPen);
                surfaceGraphics.SetClip(_dirtyGraphicsPath);

                foreach (var item in translatedPointList)
                {
                    DrawFeedbackLines(surfaceGraphics, item.Key, item.Value);
                }

                _bitmap.EndDraw();
                UpdateDraw();
            }
            catch (Exception e)
            {
                Logging.LogException(e);
                ClearSurfaces();
            }
            // this.CreateGraphics().DrawImage(bmp, 0, 0);

            // Set last stroke to copy of current stroke
            // ToList method creates value copy of stroke list and assigns it to last stroke
            _lastStroke = points.Select(p => p.Count).ToArray();
        }

        private void EnsureSurfaceVisible()
        {
            TopMost = true;
            Show();
            NativeMethods.SetWindowPos(
                Handle,
                new IntPtr(-1),
                0,
                0,
                0,
                0,
                NativeMethods.SWP.SWP_NOMOVE |
                NativeMethods.SWP.SWP_NOSIZE |
                NativeMethods.SWP.SWP_NOACTIVATE |
                NativeMethods.SWP.SWP_SHOWWINDOW);
            NativeMethods.UpdateWindow(Handle);
        }

        private void HideSurface()
        {
            Hide();
            TopMost = false;
        }

        private void EnsureDrawingSurface()
        {
            if (_bitmap != null)
                return;

            try
            {
                _bitmap = new DiBitmap(Size);
            }
            catch (ApplicationException ex)
            {
                Logging.LogException(ex);
            }
        }

        private void ResetSurface()
        {
            if (_lastStroke == null)
            {
                if (InvokeRequired) Invoke(new Action(InitializeForm));
                else InitializeForm();
            }
            else
            {
                if (InvokeRequired) Invoke(new Action(() => _settingsChanged = true));
                else _settingsChanged = true;
            }
        }

        private void InitializeForm()
        {
            // Set basic variables
            FormBorderStyle = FormBorderStyle.None;
            Name = "SurfaceForm";
            ShowIcon = false;
            StartPosition = FormStartPosition.Manual;
            Show();
            Hide();


            // Combine monitor screen sizes and set form size to combined size
            Rectangle rOutput = new Rectangle();

            foreach (Screen oScreen in Screen.AllScreens)
                rOutput = Rectangle.Union(rOutput, oScreen.Bounds);

            // 1 pixel margin for avoiding activating Focus assist
            Left = Screen.AllScreens.Min(s => s.Bounds.Left) + 1;
            Top = Screen.AllScreens.Min(s => s.Bounds.Top) + 1;
            Width = rOutput.Width - 1;
            Height = rOutput.Height - 1;
            // Store offset in class field
            _screenOffset = new Size(Location);

            InitializePen();
        }

        private void InitializePen()
        {
            var configuredWidth = AppConfig.VisualFeedbackWidth;
            _penWidth = configuredWidth <= 0 ? 0 : Math.Max(configuredWidth, MinimumVisiblePenWidth);
            _drawingPen = new Pen(ApplyVisualFeedbackAlpha(AppConfig.VisualFeedbackColor), _penWidth * DpiHelper.GetSystemDpi() / 96f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            _dirtyMarkerPen = new Pen(Color.FromArgb(30, 0, 0, 0), (_drawingPen.Width + 4f) * 1.5f)
            {
                EndCap = LineCap.Round,
                StartCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };
        }

        private Pen CreateFeedbackPen(int index)
        {
            var pen = (Pen)_drawingPen.Clone();
            var palette = VisualFeedbackPalette();
            if (palette != null && palette.Length > 0)
                pen.Color = ApplyVisualFeedbackAlpha(palette[index % palette.Length]);
            return pen;
        }

        private void DrawFeedbackLines(Graphics graphics, int strokeIndex, Point[] points)
        {
            if (points.Length < 2)
                return;

            using (var pen = CreateFeedbackPen(strokeIndex))
            {
                graphics.DrawLines(pen, points);
            }
        }

        private void DrawCompleteGesture(List<List<Point>> points)
        {
            var surfaceGraphics = _bitmap.BeginDraw();
            _graphicsPath.Reset();
            _dirtyGraphicsPath.Reset();

            for (var index = 0; index < points.Count; index++)
            {
                var translatedPoints = points[index]
                    .Where(point => !point.IsEmpty)
                    .Select(TranslatePoint)
                    .ToArray();
                if (translatedPoints.Length < 2)
                    continue;

                _graphicsPath.AddLines(translatedPoints);
                using (var pen = CreateFeedbackPen(index))
                {
                    surfaceGraphics.DrawLines(pen, translatedPoints);
                }
            }

            _bitmap.EndDraw();
        }

        private void DrawActionHint(string text)
        {
            var graphics = _bitmap.BeginDraw();
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            using (var font = new Font("Microsoft YaHei UI", 18f, FontStyle.Regular, GraphicsUnit.Point))
            {
                var textSize = graphics.MeasureString(text, font);
                var paddingX = 22f;
                var paddingY = 12f;
                var width = Math.Max(92f, textSize.Width + paddingX * 2);
                var height = textSize.Height + paddingY * 2;
                var x = (Width - width) / 2f;
                var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
                var y = workArea.Bottom - Bounds.Top - height - 12f;
                var rect = new RectangleF(x, y, width, height);

                using (var path = RoundedRectangle(rect, 10f))
                using (var brush = new SolidBrush(Color.FromArgb(222, 86, 86, 86)))
                using (var textBrush = new SolidBrush(Color.White))
                {
                    graphics.FillPath(brush, path);
                    graphics.DrawString(text, font, textBrush, x + paddingX, y + paddingY - 1f);
                }
            }

            _bitmap.EndDraw();
        }

        private static GraphicsPath RoundedRectangle(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            var diameter = radius * 2f;
            path.AddArc(rect.X, rect.Y, diameter, diameter, 180, 90);
            path.AddArc(rect.Right - diameter, rect.Y, diameter, diameter, 270, 90);
            path.AddArc(rect.Right - diameter, rect.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(rect.X, rect.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static bool IsVisualFeedbackTheme()
        {
            var value = AppConfig.VisualFeedbackColorSetting;
            return value != null && value.StartsWith("theme:", StringComparison.OrdinalIgnoreCase);
        }

        private static Color[] VisualFeedbackPalette()
        {
            var value = AppConfig.VisualFeedbackColorSetting ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value) ||
                value.Equals("#0078D4", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("theme:windows", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    Color.FromArgb(255, 0, 120, 212),
                    Color.FromArgb(255, 0, 188, 242),
                    Color.FromArgb(255, 123, 97, 255),
                    Color.FromArgb(255, 16, 124, 16)
                };
            }

            if (value.Equals("theme:pride", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    Color.FromArgb(255, 228, 3, 3),
                    Color.FromArgb(255, 255, 140, 0),
                    Color.FromArgb(255, 255, 237, 0),
                    Color.FromArgb(255, 0, 128, 38),
                    Color.FromArgb(255, 0, 77, 255),
                    Color.FromArgb(255, 117, 7, 135)
                };
            }

            if (value.Equals("theme:unity", StringComparison.OrdinalIgnoreCase))
            {
                return new[]
                {
                    Color.FromArgb(255, 0, 0, 0),
                    Color.FromArgb(255, 206, 17, 38),
                    Color.FromArgb(255, 0, 107, 63),
                    Color.FromArgb(255, 252, 209, 22)
                };
            }

            return null;
        }

        private static Color ApplyVisualFeedbackAlpha(Color color)
        {
            var alpha = Math.Max(MinimumVisibleAlpha, Math.Min(255, (int)Math.Round(AppConfig.Opacity * 255)));
            return Color.FromArgb(alpha, color.R, color.G, color.B);
        }


        private Point TranslatePoint(Point point)
        {
            // Add point offset
            return Point.Subtract(point, _screenOffset);
        }

        private void ClearSurfaces()
        {
            _lastStroke = null;
            if (_bitmap != null)
            {
                using (_bitmap)
                {
                    var g = _bitmap.BeginDraw();

                    if (_graphicsPath.PointCount > 0)
                    {
                        _graphicsPath.Widen(_dirtyMarkerPen);
                        g.SetClip(_graphicsPath);
                        g.Clear(Color.Transparent);
                    }
                    else
                    {
                        g.Clear(Color.Transparent);
                    }
                    _bitmap.EndDraw();

                    var pathDirty = _graphicsPath.PointCount > 0
                        ? Rectangle.Ceiling(_graphicsPath.GetBounds())
                        : new Rectangle(0, 0, Width, Height);
                    if (_graphicsPath.PointCount > 0)
                    {
                        pathDirty.Offset(Bounds.Location);
                        pathDirty.Intersect(Bounds);
                        pathDirty.Offset(-Bounds.X, -Bounds.Y); //挪回来变为基于窗口的坐标
                    }

                    SetDiBitmap(_bitmap, pathDirty);

                    _graphicsPath.Reset();
                    _dirtyGraphicsPath.Reset();

                }
                _bitmap = null;
            }
        }


        private void SetDiBitmap(DiBitmap bmp, Rectangle dirtyRect, byte opacity = 255)
        {
            SetHBitmap(bmp.HBitmap, Bounds, Point.Empty, dirtyRect, opacity);
        }

        //dirtyRect是绝对坐标（在多屏的情况下）
        private void SetHBitmap(IntPtr hBitmap, Rectangle newWindowBounds, Point drawAt, Rectangle dirtyRect, byte opacity)
        {
            // IntPtr screenDc = Win32.GDI32.GetDC(IntPtr.Zero);

            IntPtr memDc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            IntPtr oldBitmap = IntPtr.Zero;

            try
            {
                oldBitmap = NativeMethods.SelectObject(memDc, hBitmap);

                var winSize = new NativeMethods.Size(newWindowBounds.Width, newWindowBounds.Height);
                var winPos = new NativeMethods.Point(newWindowBounds.X, newWindowBounds.Y);

                var drawBmpAt = new NativeMethods.Point(drawAt.X, drawAt.Y);
                var blend = new NativeMethods.BLENDFUNCTION { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = opacity, AlphaFormat = AC_SRC_ALPHA };

                var updateInfo = new NativeMethods.UPDATELAYEREDWINDOWINFO
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(NativeMethods.UPDATELAYEREDWINDOWINFO)),
                    dwFlags = ULW_ALPHA,
                    hdcDst = IntPtr.Zero,
                    hdcSrc = memDc
                };
                //NativeMethods.GetDC(IntPtr.Zero);//IntPtr.Zero; //ScreenDC

                // dirtyRect.X -= _bounds.X;
                // dirtyRect.Y -= _bounds.Y;

                //dirtyRect.Offset(-_bounds.X, -_bounds.Y);
                var dirRect = new NativeMethods.RECT(dirtyRect.X, dirtyRect.Y, dirtyRect.Right, dirtyRect.Bottom);

                unsafe
                {
                    updateInfo.pblend = &blend;
                    updateInfo.pptDst = &winPos;
                    updateInfo.psize = &winSize;
                    updateInfo.pptSrc = &drawBmpAt;
                    updateInfo.prcDirty = null;
                }

                NativeMethods.UpdateLayeredWindowIndirect(Handle, ref updateInfo);
                // Debug.Assert(NativeMethods.GetLastError() == 0);

                //NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, ref topPos, ref size, memDc, ref pointSource, 0, ref blend, GDI32.ULW_ALPHA);

            }
            finally
            {

                //GDI32.ReleaseDC(IntPtr.Zero, screenDc);
                if (hBitmap != IntPtr.Zero)
                {
                    NativeMethods.SelectObject(memDc, oldBitmap);
                    //Windows.DeleteObject(hBitmap); // The documentation says that we have to use the Windows.DeleteObject... but since there is no such method I use the normal DeleteObject from Win32 GDI and it's working fine without any resource leak.
                    //Win32.DeleteObject(hBitmap);
                }
                NativeMethods.DeleteDC(memDc);
            }
        }

        private void UpdateDraw()
        {
            var pathDirty = Rectangle.Ceiling(_dirtyGraphicsPath.GetBounds());
            pathDirty.Offset(Bounds.Location);
            pathDirty.Intersect(Bounds);
            pathDirty.Offset(-Bounds.X, -Bounds.Y); //挪回来变为基于窗口的坐标

            SetDiBitmap(_bitmap, /*_pathDirtyRect*/pathDirty, 255);
        }

        private void UpdateFullSurface(byte opacity = 255)
        {
            SetDiBitmap(_bitmap, new Rectangle(0, 0, Width, Height), opacity);
        }

        #endregion

        #region Base Method Overrides

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams myParams = base.CreateParams;
                myParams.ExStyle = (int)WindowExStyleFlags.NOACTIVATE |
                                    (int)WindowExStyleFlags.TOOLWINDOW |
                                    (int)WindowExStyleFlags.TRANSPARENT |
                                    (int)WindowExStyleFlags.LAYERED;
                return myParams;
            }
        }
        #endregion
    }
}
