using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace GestureSign.PieMenu;

internal static partial class Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { internal int X, Y; }

    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(uint processId);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern bool ShowWindow(nint window, int command);
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(nint window, uint attribute, in uint value, uint size);

    internal static void RoundWindow(nint window)
    {
        const uint cornerPreference = 33;
        const uint round = 2;
        DwmSetWindowAttribute(window, cornerPreference, round, sizeof(uint));
    }
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern nint MonitorFromPoint(Point point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateEllipticRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern nint CreatePolygonRgn(Point[] points, int count, int fill);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint handle);
    [DllImport("gdi32.dll")] private static extern int CombineRgn(nint destination, nint first, nint second, int mode);

    internal static void ShapeMenu(nint window, double scale,
        IEnumerable<(Windows.Foundation.Point Center, double Radius)> discs,
        IEnumerable<(Windows.Foundation.Point Start, Windows.Foundation.Point End, double Width)> lines)
    {
        int Pixel(double value) => (int)Math.Round(value * scale);
        var result = CreateEllipticRgn(0, 0, 0, 0);
        if (result == 0) return;
        try
        {
            void Add(nint region)
            {
                if (region == 0) return;
                CombineRgn(result, result, region, 2); // RGN_OR
                DeleteObject(region);
            }
            foreach (var (center, radius) in discs)
                Add(CreateEllipticRgn(Pixel(center.X - radius), Pixel(center.Y - radius),
                    Pixel(center.X + radius), Pixel(center.Y + radius)));
            foreach (var (start, end, width) in lines)
            {
                var dx = end.X - start.X;
                var dy = end.Y - start.Y;
                var length = Math.Sqrt(dx * dx + dy * dy);
                if (length < 1) continue;
                var offsetX = -dy / length * width / 2;
                var offsetY = dx / length * width / 2;
                Point At(Windows.Foundation.Point p, double sign) => new()
                {
                    X = Pixel(p.X + sign * offsetX), Y = Pixel(p.Y + sign * offsetY)
                };
                Add(CreatePolygonRgn([At(start, 1), At(end, 1), At(end, -1), At(start, -1)], 4, 1));
            }
            if (SetWindowRgn(window, result, true) != 0) result = 0; // OS now owns the region.
        }
        finally { if (result != 0) DeleteObject(result); }
    }

    internal static (RectInt32 Area, double Scale) MonitorAt(Point cursor)
    {
        var area = DisplayArea.GetFromPoint(new PointInt32(cursor.X, cursor.Y), DisplayAreaFallback.Nearest);
        var scale = GetDpiForMonitor(MonitorFromPoint(cursor, 2), 0, out var dpi, out _) == 0 ? dpi / 96d : 1d;
        return (area.WorkArea, scale);
    }

    internal static void Open(string value, bool command)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidDataException("This action has no target.");
        if (command)
        {
            var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
            {
                UseShellExecute = false, CreateNoWindow = true
            };
            start.Arguments = "/d /s /c \"" + value + "\"";
            using var child = Process.Start(start);
            return;
        }
        using var shell = Process.Start(new ProcessStartInfo(Environment.ExpandEnvironmentVariables(value)) { UseShellExecute = true });
    }
}
