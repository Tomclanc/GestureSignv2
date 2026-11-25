using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace GestureSign.PieMenu;

internal sealed class MenuConditions
{
    public string AppName { get; set; } = "";
    public string WindowName { get; set; } = "";
    public double? XMin { get; set; }
    public double? XMax { get; set; }
    public double? YMin { get; set; }
    public double? YMax { get; set; }

    internal static bool IsValidPattern(string pattern) =>
        !pattern.StartsWith('/') || ParseRegex(pattern) is not null;

    internal int Score(Foreground context)
    {
        if (!Matches(AppName, context.App) || !Matches(WindowName, context.Title)) return -1;
        if (context.X < XMin || context.X > XMax || context.Y < YMin || context.Y > YMax) return -1;
        var hasArea = XMin.HasValue || XMax.HasValue || YMin.HasValue || YMax.HasValue;
        return (AppName.Length > 0 ? 1 : 0) + (WindowName.Length > 0 ? 1 : 0) + (hasArea ? 1 : 0);
    }

    private static bool Matches(string pattern, string value)
    {
        if (!pattern.StartsWith('/')) return value.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        try { return ParseRegex(pattern)?.IsMatch(value) == true; }
        catch (RegexMatchTimeoutException) { return false; }
    }

    private static Regex? ParseRegex(string value)
    {
        var end = value.LastIndexOf('/');
        if (end == 0 || value.Length > 2048) return null;
        var options = RegexOptions.CultureInvariant | RegexOptions.ECMAScript;
        foreach (var flag in value.AsSpan(end + 1))
        {
            switch (flag)
            {
                case 'i': options |= RegexOptions.IgnoreCase; break;
                case 'm': options |= RegexOptions.Multiline; break;
                case 'g':
                case 'y': break;
                default: return null;
            }
        }
        var pattern = value[1..end];
        if (value.AsSpan(end + 1).Contains('y')) pattern = @"\G(?:" + pattern + ")";
        try { return new Regex(pattern, options, TimeSpan.FromMilliseconds(30)); }
        catch (ArgumentException) { return null; }
    }

    internal readonly record struct Foreground(string App, string Title, double X, double Y)
    {
        internal static Foreground Read()
        {
            var hwnd = Desktop.GetForegroundWindow();
            var owner = GetAncestor(hwnd, 3);
            if (owner != 0) hwnd = owner;
            var title = new StringBuilder(1024);
            GetWindowText(hwnd, title, title.Capacity);
            GetWindowThreadProcessId(hwnd, out var pid);
            var name = "";
            try
            {
                using var process = Process.GetProcessById(pid);
                name = process.ProcessName + ".exe";
            }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            Desktop.GetCursorPos(out var pointer);
            var (bounds, scale) = Desktop.MonitorAt(pointer);
            return new Foreground(name, title.ToString(),
                bounds.X + (pointer.X - bounds.X) / scale, bounds.Y + (pointer.Y - bounds.Y) / scale);
        }

        [DllImport("user32.dll", EntryPoint = "GetWindowTextW", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(nint hwnd, out int processId);
        [DllImport("user32.dll")]
        private static extern nint GetAncestor(nint hwnd, uint flags);
    }
}
