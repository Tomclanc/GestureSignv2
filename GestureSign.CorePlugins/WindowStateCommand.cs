using System;
using System.Windows.Forms;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using ManagedWinapi.Windows;

namespace GestureSign.CorePlugins
{
    internal static class WindowStateCommand
    {
        internal static bool Execute(PointInfo point, bool minimize)
        {
            var target = point?.Window?.TopLevelWindow;
            try
            {
                if (target == null || target.HWnd == IntPtr.Zero)
                    return false;
                var className = target.ClassName;
                if (className is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                    or "ImmersiveBackgroundWindow" or "ImmersiveLauncher" ||
                    (target.ExtendedStyle & WindowExStyleFlags.TOOLWINDOW) != 0)
                {
                    Logging.LogMessage($"Window state skipped. TargetHwnd={target.HWnd}, Class={className}, Reason=ShellOrToolWindow");
                    return false;
                }
                var before = target.WindowState;
                var requested = minimize ? FormWindowState.Minimized :
                    before == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                var accepted = target.RequestWindowState(requested);
                Logging.LogMessage($"Window state requested. CapturedHwnd={point.WindowHandle}, TargetHwnd={target.HWnd}, Class={className}, Before={before}, Requested={requested}, Accepted={accepted}");
                return accepted;
            }
            catch (Exception error)
            {
                Logging.LogException(error);
                return false;
            }
        }
    }
}
