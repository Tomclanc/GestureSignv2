using System;
using System.Runtime.InteropServices;

namespace GestureSign.CorePlugins.ScreenBrightness
{
    internal static class NativeBrightnessFlyout
    {
        // Undocumented Windows Shell service. Keep failures isolated from the
        // brightness operation; do not simulate volume keys to open this UI.
        // IFlyoutDisplay's brightness selector is 3 on Windows 11.
        internal static bool TryShow()
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return false;
            object shell = null;
            object display = null;
            IntPtr pointer = IntPtr.Zero;
            try
            {
                var type = Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239"));
                shell = Activator.CreateInstance(type);
                var id = typeof(IFlyoutDisplay).GUID;
                var result = ((IShellServiceProvider)shell).QueryService(ref id, ref id, out pointer);
                if (result < 0 || pointer == IntPtr.Zero) return false;
                display = Marshal.GetObjectForIUnknown(pointer);
                return ((IFlyoutDisplay)display).ShowFlyout(3, 0) >= 0;
            }
            catch { return false; }
            finally
            {
                if (pointer != IntPtr.Zero) Marshal.Release(pointer);
                if (display != null && Marshal.IsComObject(display)) Marshal.ReleaseComObject(display);
                if (shell != null && Marshal.IsComObject(shell)) Marshal.ReleaseComObject(shell);
            }
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellServiceProvider
        {
            [PreserveSig]
            int QueryService(ref Guid service, ref Guid iid, out IntPtr result);
        }

        [ComImport, Guid("41F9D2FB-7834-4AB6-8B1B-73E74064B465"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFlyoutDisplay
        {
            [PreserveSig]
            int ShowFlyout(int kind, uint flags);
        }
    }
}
