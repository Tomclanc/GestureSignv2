using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace GestureSign.CorePlugins.Steps
{
    /// <summary>
    /// Reads the selection and current folder of a file manager window. File Explorer is read
    /// through the Shell.Application automation object; file managers that draw their own window
    /// have no automation tree and are read from the state file they keep instead.
    /// Call on an STA thread.
    /// </summary>
    internal static class ExplorerSelection
    {
        /// <summary>Selected file system paths, or null when window is not a File Explorer window this can read.</summary>
        public static string[] SelectedFiles(IntPtr window)
        {
            var view = FindExplorerView(RootOf(window));
            if (view == null)
                return null;
            var files = new List<string>();
            foreach (dynamic item in view.Document.SelectedItems())
            {
                string path = item.Path;
                if (IsFileSystemPath(path))
                    files.Add(path);
            }
            return files.ToArray();
        }

        /// <summary>The folder the window shows: File Explorer, the desktop folder for the desktop, Pulse, otherwise null.</summary>
        public static string CurrentFolder(IntPtr window)
        {
            var root = RootOf(window);
            if (IsDesktop(root))
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var view = FindExplorerView(root);
            if (view != null)
            {
                string path = view.Document.Folder.Self.Path;
                return IsFileSystemPath(path) ? path : null;
            }
            return PulseFolder(root);
        }

        /// <summary>
        /// Pulse draws its own window (class PulseMainWindow) and publishes no UI Automation tree,
        /// so neither Shell.Application nor automation can tell which folder is open. It does write
        /// the active tab of every window to %LOCALAPPDATA%\Pulse\session.json as it navigates,
        /// which is what this reads: the window is matched by rectangle, then by its title, then the
        /// only window there is. A changed private format simply yields no folder, as before.
        /// </summary>
        private static string PulseFolder(IntPtr root)
        {
            if (ClassName(root) != "PulseMainWindow")
                return null;
            var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pulse", "session.json");
            string text;
            try
            {
                if (!File.Exists(file))
                    return null;
                text = File.ReadAllText(file);
            }
            catch
            {
                return null;
            }

            try
            {
                using var document = JsonDocument.Parse(text);
                if (!document.RootElement.TryGetProperty("windows", out var windows) || windows.ValueKind != JsonValueKind.Array)
                    return null;

                var candidates = new List<JsonElement>();
                foreach (var candidate in windows.EnumerateArray())
                    candidates.Add(candidate);

                JsonElement? chosen = null;
                foreach (var candidate in candidates)
                {
                    if (SameRectangle(candidate, root))
                    {
                        chosen = candidate;
                        break;
                    }
                }
                if (chosen == null && candidates.Count == 1)
                    chosen = candidates[0];
                if (chosen == null)
                    chosen = WindowTitled(candidates, WindowTextOf(root));
                return chosen is JsonElement state ? ActiveFolderOf(state) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>The folder of the tab the window shows, from one entry of Pulse's session file.</summary>
        private static string ActiveFolderOf(JsonElement window)
        {
            if (!window.TryGetProperty("layoutTabs", out var tabs) || tabs.ValueKind != JsonValueKind.Array)
                return null;
            var index = window.TryGetProperty("activeTab", out var active) && active.ValueKind == JsonValueKind.Number ? active.GetInt32() : 0;
            if (index < 0 || index >= tabs.GetArrayLength())
                return null;
            var tab = tabs[index];
            if (!tab.TryGetProperty("panes", out var panes) || panes.ValueKind != JsonValueKind.Array || panes.GetArrayLength() == 0)
                return null;

            // "focused" holds the index of the pane that has the focus (0 with a single pane).
            var pane = panes[0];
            if (tab.TryGetProperty("focused", out var focused) && focused.ValueKind == JsonValueKind.Number)
            {
                var focusedIndex = focused.GetInt32();
                if (focusedIndex >= 0 && focusedIndex < panes.GetArrayLength())
                    pane = panes[focusedIndex];
            }

            var path = pane.TryGetProperty("path", out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            path = WithoutLongPathPrefix(path);
            return path != null && Directory.Exists(path) ? path : null;
        }

        /// <summary>
        /// The window whose active folder name starts the title, used when a window was moved after
        /// the session file was last written and the rectangle no longer matches.
        /// </summary>
        private static JsonElement? WindowTitled(List<JsonElement> windows, string title)
        {
            if (string.IsNullOrEmpty(title))
                return null;
            foreach (var window in windows)
            {
                var folder = ActiveFolderOf(window);
                if (folder == null)
                    continue;
                var name = Path.GetFileName(folder.TrimEnd('\\'));
                if (name.Length > 0 && title.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase))
                    return window;
            }
            return null;
        }

        private static bool SameRectangle(JsonElement window, IntPtr handle)
        {
            if (!WindowRectangle(handle, out var rectangle))
                return false;
            return Near(window, "left", rectangle.Left) && Near(window, "top", rectangle.Top)
                && Near(window, "right", rectangle.Right) && Near(window, "bottom", rectangle.Bottom);
        }

        private static bool Near(JsonElement window, string name, int value)
            => window.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number
                && Math.Abs(element.GetInt32() - value) <= 2;

        /// <summary>Drops the \\?\ (and \\?\UNC\) prefix Pulse stores paths with.</summary>
        private static string WithoutLongPathPrefix(string path)
        {
            if (string.IsNullOrEmpty(path))
                return null;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
                path = @"\\" + path.Substring(8);
            else if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
                path = path.Substring(4);
            return IsFileSystemPath(path) ? path : null;
        }

        public static IntPtr RootOf(IntPtr window)
        {
            if (window == IntPtr.Zero)
                window = GetForegroundWindow();
            var root = GetAncestor(window, GA_ROOT);
            return root == IntPtr.Zero ? window : root;
        }

        private static bool IsFileSystemPath(string path)
            => !string.IsNullOrEmpty(path) && !path.StartsWith("::", StringComparison.Ordinal) && Path.IsPathRooted(path);

        private static bool IsDesktop(IntPtr root)
        {
            var name = ClassName(root);
            return name == "Progman" || name == "WorkerW";
        }

        /// <summary>The Shell window object of the active tab of a File Explorer window.</summary>
        private static dynamic FindExplorerView(IntPtr root)
        {
            var name = ClassName(root);
            if (name != "CabinetWClass" && name != "ExploreWClass")
                return null;
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null)
                return null;

            // Windows 11 tabs share the frame window; the first tab child is the active one.
            var activeTab = FindWindowEx(root, IntPtr.Zero, "ShellTabWindowClass", null);
            dynamic shell = Activator.CreateInstance(shellType);
            object only = null;
            var count = 0;
            foreach (dynamic window in shell.Windows())
            {
                long handle;
                try { handle = Convert.ToInt64((object)window.HWND); }
                catch { continue; }
                if (new IntPtr(handle) != root)
                    continue;
                count++;
                if (activeTab != IntPtr.Zero && TabWindowOf(window) == activeTab)
                    return window;
                only = window;
            }
            return count == 1 ? only : null;
        }

        private static IntPtr TabWindowOf(object window)
        {
            if (window is not IComServiceProvider provider)
                return IntPtr.Zero;
            var service = SID_STopLevelBrowser;
            var iid = typeof(IShellBrowser).GUID;
            if (provider.QueryService(ref service, ref iid, out var pointer) != 0 || pointer == IntPtr.Zero)
                return IntPtr.Zero;
            try
            {
                var browser = (IShellBrowser)Marshal.GetObjectForIUnknown(pointer);
                return browser.GetWindow(out var handle) == 0 ? handle : IntPtr.Zero;
            }
            catch
            {
                return IntPtr.Zero;
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }

        private static string ClassName(IntPtr hWnd)
        {
            var buffer = new StringBuilder(256);
            return GetClassName(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
        }

        private static string WindowTextOf(IntPtr hWnd)
        {
            var length = GetWindowTextLength(hWnd);
            if (length <= 0)
                return "";
            var buffer = new StringBuilder(length + 1);
            return GetWindowText(hWnd, buffer, buffer.Capacity) > 0 ? buffer.ToString() : "";
        }

        private static bool WindowRectangle(IntPtr hWnd, out RECT rectangle) => GetWindowRect(hWnd, out rectangle);

        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int GA_ROOT = 2;
        private static readonly Guid SID_STopLevelBrowser = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
        private interface IComServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr pointer);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214E2-0000-0000-C000-000000000046")]
        private interface IShellBrowser
        {
            // IOleWindow::GetWindow is the first method.
            [PreserveSig] int GetWindow(out IntPtr handle);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, int flags);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hWnd, StringBuilder name, int maxCount);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rectangle);
    }
}
