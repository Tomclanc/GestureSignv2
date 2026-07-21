using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
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

        public string Name => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartClose.Name");
        public string Category => "Windows";
        public string Description => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartClose.Description");
        public bool IsAction => true;
        public object GUI => null;
        // Touchpad capture already locks the foreground target. Reactivating a stale
        // point-derived window here can redirect the shortcut to a shell surface.
        public bool ActivateWindowDefault => false;
        public object Icon => IconSource.Window;
        public IHostControl HostControl { get; set; }

        public void Initialize()
        {
        }

        public bool Gestured(PointInfo actionPoint)
        {
            if (actionPoint?.Window == null)
                return false;

            // Keep the same target-window path used by the known-good 16.4.21 build.
            var targetWindow = actionPoint.Window;
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
