using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.CorePlugins
{
    public sealed class SmartClose : IPlugin
    {
        private static readonly HashSet<string> ControlWApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedge",
            "chrome",
            "firefox",
            "brave",
            "opera",
            "vivaldi",
            "explorer",
            "weixin",
            "wechat",
            "wechatappex",
            "notepad",
            "devenv"
        };

        private static readonly HashSet<string> VisualStudioCodeApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "code - insiders",
            "codium",
            "vscodium"
        };

        private static readonly HashSet<string> ControlShiftWApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windowsterminal",
            "windowsterminalpreview"
        };

        private static readonly HashSet<string> AltF4Applications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Clash Party"
        };

        private static readonly HashSet<string> IgnoredWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd"
        };

        private const string OverflowShellWindowClass = "TopLevelWindowForOverflowXamlIsland";
        private const string ApplicationFrameHostProcess = "ApplicationFrameHost";
        private const string ApplicationFrameWindowClass = "ApplicationFrameWindow";
        private const uint WmClose = 0x0010;
        private const uint WmSysCommand = 0x0112;
        private const uint ScClose = 0xF060;

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "PostMessageW")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool PostWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        public string Name => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartClose.Name");
        public string Category => "Windows";
        public string Description => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartClose.Description");
        public bool IsAction => true;
        public object GUI => null;
        // Smart Close sends a keyboard shortcut, so it must run against the
        // window captured at gesture start. PluginManager activates the target
        // and waits until it is the real foreground window before injecting it.
        public bool ActivateWindowDefault => true;
        public object Icon => IconSource.Window;
        public IHostControl HostControl { get; set; }

        public void Initialize()
        {
        }

        public bool Gestured(PointInfo actionPoint)
        {
            if (actionPoint?.Window == null)
                return false;

            // The activation phase now performs a real left click at the
            // current pointer position. Use the window that is actually in
            // the foreground after that click; the HWND captured at gesture
            // start may be a shell surface or an obsolete UWP child.
            var targetWindow = SystemWindow.ForegroundWindow ?? actionPoint.Window;
            if (targetWindow == null)
                return false;

            if (actionPoint.Window != null && targetWindow.HWnd != actionPoint.Window.HWnd)
                Logging.LogMessage($"Smart close target refreshed after activation click. CapturedHwnd={actionPoint.Window.HWnd}, ForegroundHwnd={targetWindow.HWnd}");
            var className = targetWindow.ClassName ?? string.Empty;
            if (IgnoredWindowClasses.Contains(className) ||
                string.Equals(className, OverflowShellWindowClass, StringComparison.OrdinalIgnoreCase))
            {
                var clashPartyWindow = FindClashPartyWindowAtGesturePoint(actionPoint);
                if (clashPartyWindow == null)
                {
                    if (IgnoredWindowClasses.Contains(className))
                    {
                        Logging.LogMessage($"Smart close skipped. WindowClass={className}, Reason=ShellSurface");
                        return false;
                    }
                }
                else
                {
                    Logging.LogMessage($"Smart close target recovered. Reason=ClashPartyAtGesturePoint, ShellHwnd={targetWindow.HWnd}, TargetHwnd={clashPartyWindow.HWnd}, StartPoint={actionPoint.PointLocation[0].X},{actionPoint.PointLocation[0].Y}");
                    targetWindow = clashPartyWindow;
                    className = targetWindow.ClassName ?? string.Empty;
                }
            }

            var processName = GetProcessName(targetWindow.ProcessId);
            var windowTitle = VisualStudioCodeApplications.Contains(processName)
                ? targetWindow.Title ?? string.Empty
                : string.Empty;
            var shortcut = SelectShortcut(processName, className, windowTitle);
            Logging.LogMessage($"Smart close selected. Process={processName ?? "(unknown)"}, WindowClass={className}, WindowTitle={windowTitle}, Shortcut={shortcut}");

            // Microsoft Store and other packaged WinUI apps are hosted by
            // ApplicationFrameHost. A global Alt+F4 injected from the daemon
            // can be swallowed by the shell while the gesture process is not
            // the foreground input queue. FastGestures closes this wrapper by
            // sending the native close message to the captured frame instead.
            if (IsApplicationFrameWindow(processName, className))
            {
                CloseApplicationFrame(targetWindow);
                return true;
            }

            if (shortcut == CloseShortcut.AltF4 && AltF4Applications.Contains(processName) && IsCurrentProcessElevated())
            {
                SystemWindow.ForegroundWindow = targetWindow;
                Thread.Sleep(40);
                SendShortcut(shortcut);
                Logging.LogMessage($"Smart close sent directly from elevated daemon. Process={processName}, TargetHwnd={targetWindow.HWnd}");
                return true;
            }

            if (shortcut == CloseShortcut.AltF4 && AltF4Applications.Contains(processName) &&
                TryRunUiAccessAltF4Helper(targetWindow.HWnd))
            {
                Logging.LogMessage($"Smart close delegated to UIAccess helper. Process={processName}, TargetHwnd={targetWindow.HWnd}");
                return true;
            }

            SendShortcut(shortcut);
            return true;
        }

        private static void SendShortcut(CloseShortcut shortcut)
        {
            var keyboard = new InputSimulator().Keyboard;
            switch (shortcut)
            {
                case CloseShortcut.ControlW:
                    keyboard.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_W);
                    break;
                case CloseShortcut.ControlShiftW:
                    keyboard.ModifiedKeyStroke(
                        new[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LSHIFT },
                        VirtualKeyCode.VK_W);
                    break;
                default:
                    keyboard.ModifiedKeyStroke(VirtualKeyCode.LMENU, VirtualKeyCode.F4);
                    break;
            }

        }

        private static bool IsApplicationFrameWindow(string processName, string className)
        {
            return string.Equals(processName, ApplicationFrameHostProcess, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, ApplicationFrameWindowClass, StringComparison.OrdinalIgnoreCase);
        }

        private static void CloseApplicationFrame(SystemWindow targetWindow)
        {
            try
            {
                // UWP frame hosts can ignore WM_CLOSE on the outer frame while
                // handling the system-command close on the XAML child. Send
                // both messages to the frame and its CoreWindow descendants,
                // then post WM_CLOSE as the asynchronous fallback.
                var frameWindow = targetWindow.TopLevelWindow ?? targetWindow;
                var windows = new List<SystemWindow> { frameWindow };
                if (targetWindow.HWnd != frameWindow.HWnd)
                    windows.Add(targetWindow);
                try
                {
                    windows.AddRange(frameWindow.AllDescendantWindows
                        .Where(window => string.Equals(window.ClassName, "Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)));
                }
                catch
                {
                    // A disappearing packaged window is harmless; the outer
                    // frame is still attempted below.
                }

                foreach (var window in windows.Distinct())
                {
                    SendWindowMessage(window.HWnd, WmSysCommand, new IntPtr(unchecked((int)ScClose)), IntPtr.Zero);
                    SendWindowMessage(window.HWnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                    PostWindowMessage(window.HWnd, WmClose, IntPtr.Zero, IntPtr.Zero);
                }

                Logging.LogMessage($"Smart close sent native close sequence to packaged app frame. TargetHwnd={targetWindow.HWnd}, FrameHwnd={frameWindow.HWnd}, Windows={windows.Count}");

                // Some Store apps handle the frame messages asynchronously (or
                // ignore them altogether) but do honor Alt+F4 once their frame
                // owns the foreground input queue. Give the native close a
                // brief chance, then use the same foreground-verified keyboard
                // fallback without stealing input from another window.
                Thread.Sleep(80);
                var foreground = SystemWindow.ForegroundWindow;
                if (foreground != null && foreground.HWnd == frameWindow.HWnd)
                {
                    SendShortcut(CloseShortcut.AltF4);
                    Logging.LogMessage($"Smart close sent Alt+F4 fallback to packaged app frame. TargetHwnd={targetWindow.HWnd}, FrameHwnd={frameWindow.HWnd}");
                }
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                try
                {
                    targetWindow.PostClose();
                    Logging.LogMessage($"Smart close posted to packaged app frame after native close failure. TargetHwnd={targetWindow.HWnd}");
                }
                catch
                {
                }
            }
        }

        public bool Deserialize(string serializedData)
        {
            return true;
        }

        public string Serialize()
        {
            return string.Empty;
        }

        internal static string SelectShortcutName(string processName, string className)
        {
            return SelectShortcut(processName, className, string.Empty).ToString();
        }

        internal static string SelectShortcutName(string processName, string className, string windowTitle, bool? hasVisualStudioCodeEditorTab)
        {
            return SelectShortcut(processName, className, windowTitle, hasVisualStudioCodeEditorTab).ToString();
        }

        private static CloseShortcut SelectShortcut(string processName, string className, string windowTitle, bool? visualStudioCodeEditorTab = null)
        {
            if (AltF4Applications.Contains(processName))
                return CloseShortcut.AltF4;

            if (ControlShiftWApplications.Contains(processName) ||
                string.Equals(className, "CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase))
            {
                return CloseShortcut.ControlShiftW;
            }

            if (VisualStudioCodeApplications.Contains(processName))
            {
                if (visualStudioCodeEditorTab.HasValue)
                    return visualStudioCodeEditorTab.Value ? CloseShortcut.ControlW : CloseShortcut.ControlShiftW;

                return IsEmptyVisualStudioCodeTitle(windowTitle)
                    ? CloseShortcut.ControlShiftW
                    : CloseShortcut.ControlW;
            }

            if (ControlWApplications.Contains(processName) ||
                string.Equals(className, "CabinetWClass", StringComparison.OrdinalIgnoreCase))
            {
                return CloseShortcut.ControlW;
            }

            return CloseShortcut.AltF4;
        }

        private static bool TryRunUiAccessAltF4Helper(IntPtr windowHandle)
        {
            try
            {
                var helperPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GestureSign.UIAccess.exe");
                if (!File.Exists(helperPath))
                    return false;

                Process.Start(new ProcessStartInfo
                {
                    FileName = helperPath,
                    Arguments = $"--uiaccess-alt-f4 {windowHandle.ToInt64()}",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static SystemWindow FindUniqueVisibleClashPartyMainWindow()
        {
            SystemWindow candidate = null;
            foreach (var window in SystemWindow.AllToplevelWindows)
            {
                try
                {
                    if (window == null ||
                        window.HWnd == IntPtr.Zero ||
                        !window.Visible ||
                        !string.Equals(window.Title?.Trim(), "Clash Party", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(window.ClassName, "Chrome_WidgetWin_1", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(GetProcessName(window.ProcessId), "Clash Party", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (candidate != null)
                        return null;

                    candidate = window;
                }
                catch
                {
                    // Protected and disappearing windows are not candidates.
                }
            }

            return candidate;
        }

        private static SystemWindow FindClashPartyWindowAtGesturePoint(PointInfo actionPoint)
        {
            if (actionPoint?.PointLocation == null || actionPoint.PointLocation.Count == 0)
                return null;

            var candidate = FindUniqueVisibleClashPartyMainWindow();
            if (candidate == null)
                return null;

            try
            {
                Rectangle bounds = candidate.Rectangle;
                return bounds.Contains(actionPoint.PointLocation[0]) ? candidate : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsCurrentProcessElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool IsEmptyVisualStudioCodeTitle(string windowTitle)
        {
            if (string.IsNullOrWhiteSpace(windowTitle))
                return false;

            var title = windowTitle.Trim();
            return string.Equals(title, "Visual Studio Code", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "Visual Studio Code - Insiders", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "Code - OSS", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "VSCodium", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetProcessName(int processId)
        {
            if (processId <= 0)
                return null;

            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                return null;
            }
        }

        private enum CloseShortcut
        {
            ControlW,
            ControlShiftW,
            AltF4
        }
    }
}
