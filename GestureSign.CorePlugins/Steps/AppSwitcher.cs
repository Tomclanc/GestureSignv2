using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using ManagedWinapi.Windows;

namespace GestureSign.CorePlugins.Steps
{
    /// <summary>Launches a program, or switches to its window when it is already running.</summary>
    internal static class AppSwitcher
    {
        public static string LaunchOrActivate(string path, string arguments, string processName, string titleFilter, string whenActive, bool hideLaunchWindow)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("No program was given.");

            var launchPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            // The window may belong to another program than the one launched (a .bat file or a launcher).
            var exePath = string.IsNullOrWhiteSpace(processName)
                ? launchPath
                : Environment.ExpandEnvironmentVariables(processName.Trim().Trim('"'));
            if (string.Equals(Path.GetExtension(exePath), ".lnk", StringComparison.OrdinalIgnoreCase))
                exePath = ResolveShortcut(exePath) ?? exePath;
            var exeName = Path.GetFileName(exePath);
            if (string.IsNullOrEmpty(Path.GetExtension(exeName)))
                exeName += ".exe";
            var fullPath = Path.IsPathRooted(exePath) ? Path.GetFullPath(exePath) : null;

            var windows = FindWindows(fullPath, exeName, titleFilter);
            if (windows.Count == 0)
            {
                Launch(launchPath, arguments, hideLaunchWindow);
                return "launched";
            }

            var foreground = GetAncestor(GetForegroundWindow(), GA_ROOT);
            var frontIndex = windows.IndexOf(foreground);
            if (frontIndex >= 0)
            {
                switch ((whenActive ?? "").Trim().ToLowerInvariant())
                {
                    case "none":
                        return "none";
                    case "cycle":
                        if (windows.Count < 2)
                            return "none";
                        // EnumWindows lists windows top to bottom, so the last one has waited longest.
                        Activate(windows[windows.Count - 1]);
                        return "activated";
                    default:
                        ShowWindow(foreground, SW_MINIMIZE);
                        return "minimized";
                }
            }

            Activate(windows[0]);
            return "activated";
        }

        private static void Launch(string launchPath, string arguments, bool hideConsole)
        {
            var workingDirectory = Path.IsPathRooted(launchPath) ? Path.GetDirectoryName(Path.GetFullPath(launchPath)) ?? "" : "";
            var extension = Path.GetExtension(launchPath);
            ProcessStartInfo startInfo;
            if (hideConsole && (string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase)))
            {
                // cmd /c ""script" args": the outer quotes keep the inner ones intact.
                startInfo = new ProcessStartInfo("cmd.exe", $"/c \"\"{launchPath}\" {arguments}\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = workingDirectory
                };
            }
            else
            {
                startInfo = new ProcessStartInfo(launchPath, arguments ?? "")
                {
                    UseShellExecute = true,
                    WorkingDirectory = workingDirectory
                };
            }
            using var started = Process.Start(startInfo);
        }

        private static void Activate(IntPtr hWnd)
        {
            if (IsIconic(hWnd))
                ShowWindow(hWnd, SW_RESTORE);
            SystemWindow.ActivateWindow(new SystemWindow(hWnd));
        }

        /// <summary>Visible top-level application windows of the program, in z-order (topmost first).</summary>
        private static List<IntPtr> FindWindows(string fullPath, string exeName, string titleFilter)
        {
            var byPath = new List<IntPtr>();
            var byName = new List<IntPtr>();
            var imagePaths = new Dictionary<uint, string>();
            var title = new StringBuilder(512);
            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd) || GetWindow(hWnd, GW_OWNER) != IntPtr.Zero)
                    return true;
                if ((GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0)
                    return true;
                title.Clear();
                GetWindowText(hWnd, title, title.Capacity);
                if (title.Length == 0)
                    return true;
                if (!string.IsNullOrWhiteSpace(titleFilter)
                    && title.ToString().IndexOf(titleFilter.Trim(), StringComparison.OrdinalIgnoreCase) < 0)
                    return true;

                GetWindowThreadProcessId(hWnd, out var pid);
                if (!imagePaths.TryGetValue(pid, out var image))
                    imagePaths[pid] = image = ProcessImagePath(pid);
                if (string.IsNullOrEmpty(image))
                    return true;
                if (fullPath != null && string.Equals(image, fullPath, StringComparison.OrdinalIgnoreCase))
                    byPath.Add(hWnd);
                else if (string.Equals(Path.GetFileName(image), exeName, StringComparison.OrdinalIgnoreCase))
                    byName.Add(hWnd);
                return true;
            }, IntPtr.Zero);
            // An exact path match wins; otherwise any running copy with the same file name.
            return byPath.Count > 0 ? byPath : byName;
        }

        private static string ProcessImagePath(uint pid)
        {
            var handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero)
                return null;
            try
            {
                var buffer = new StringBuilder(1024);
                var size = buffer.Capacity;
                return QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static string ResolveShortcut(string lnkPath)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(lnkPath, 0);
                var target = new StringBuilder(1024);
                link.GetPath(target, target.Capacity, IntPtr.Zero, 0);
                return target.Length > 0 ? target.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        private const int GA_ROOT = 2;
        private const uint GW_OWNER = 4;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint command);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, int flags);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int command);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
        private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        }

        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("0000010b-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid classId);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        }
    }
}
