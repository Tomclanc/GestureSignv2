using System;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.RadialMenu
{
    /// <summary>
    /// Tells whether the optional Quick Actions component is showing its own
    /// radial menu right now.
    ///
    /// That menu runs in a separate process as a transparent topmost overlay, so
    /// the daemon's own input state (PointCapture) cannot see it. The overlay
    /// window class is the only state both processes share: it exists while the
    /// window is created and is hidden when the menu closes. While it is
    /// visible, the middle button belongs to that menu (it selects the hovered
    /// item), so the built-in middle-button menu must not open on top of it.
    /// </summary>
    internal static class QuickActionsPresence
    {
        /// <summary>Window class registered by GestureSign.PieMenu's WebMenuWindow.</summary>
        private const string OverlayClass = "GestureSign.PieMenu.WebOverlay";

        internal static bool IsMenuOpen()
        {
            var window = FindWindowW(OverlayClass, null);
            return window != IntPtr.Zero && IsWindowVisible(window);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindowW(string className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr window);
    }
}
