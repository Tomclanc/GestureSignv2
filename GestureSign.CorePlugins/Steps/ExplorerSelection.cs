using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace GestureSign.CorePlugins.Steps
{
    /// <summary>
    /// Reads the selection and current folder of a File Explorer window through the
    /// Shell.Application automation object. Call on an STA thread.
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

        /// <summary>The folder shown in the File Explorer window, the desktop folder for the desktop, otherwise null.</summary>
        public static string CurrentFolder(IntPtr window)
        {
            var root = RootOf(window);
            if (IsDesktop(root))
                return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var view = FindExplorerView(root);
            if (view == null)
                return null;
            string path = view.Document.Folder.Self.Path;
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
    }
}
