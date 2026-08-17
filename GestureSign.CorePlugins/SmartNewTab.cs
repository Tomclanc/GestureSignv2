using GestureSign.Common.Localization;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.CorePlugins
{
    public sealed class SmartNewTab : IPlugin
    {
        private static readonly HashSet<string> ControlTApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "msedge",
            "chrome",
            "firefox",
            "brave",
            "opera",
            "vivaldi",
            "explorer",
            "notepad"
        };

        private static readonly HashSet<string> ControlShiftTApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "windowsterminal",
            "windowsterminalpreview"
        };

        private static readonly HashSet<string> ControlNApplications = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "code",
            "code - insiders",
            "codium",
            "vscodium"
        };

        private static readonly HashSet<string> IgnoredWindowClasses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Progman",
            "WorkerW",
            "Shell_TrayWnd",
            "Shell_SecondaryTrayWnd",
            "TopLevelWindowForOverflowXamlIsland"
        };

        public string Name => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartNewTab.Name");
        public string Category => "Windows";
        public string Description => LocalizationProvider.Instance.GetTextValue("CorePlugins.SmartNewTab.Description");
        public bool IsAction => true;
        public object GUI => null;
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

            var targetWindow = actionPoint.Window;
            var className = targetWindow.ClassName ?? string.Empty;
            if (IgnoredWindowClasses.Contains(className))
            {
                Logging.LogMessage($"Smart new tab skipped. WindowClass={className}, Reason=ShellSurface");
                return false;
            }

            var processName = GetProcessName(targetWindow.ProcessId);
            var shortcut = SelectShortcut(processName, className);
            Logging.LogMessage($"Smart new tab selected. Process={processName ?? "(unknown)"}, WindowClass={className}, Shortcut={shortcut}");
            SendShortcut(shortcut);
            return true;
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
            return SelectShortcut(processName, className).ToString();
        }

        private static NewTabShortcut SelectShortcut(string processName, string className)
        {
            if (ControlShiftTApplications.Contains(processName) ||
                string.Equals(className, "CASCADIA_HOSTING_WINDOW_CLASS", StringComparison.OrdinalIgnoreCase))
            {
                return NewTabShortcut.ControlShiftT;
            }

            if (ControlTApplications.Contains(processName) ||
                string.Equals(className, "CabinetWClass", StringComparison.OrdinalIgnoreCase))
            {
                return NewTabShortcut.ControlT;
            }

            if (ControlNApplications.Contains(processName))
                return NewTabShortcut.ControlN;

            return NewTabShortcut.ControlN;
        }

        private static void SendShortcut(NewTabShortcut shortcut)
        {
            var keyboard = new InputSimulator().Keyboard;
            switch (shortcut)
            {
                case NewTabShortcut.ControlT:
                    keyboard.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_T);
                    break;
                case NewTabShortcut.ControlShiftT:
                    keyboard.ModifiedKeyStroke(
                        new[] { VirtualKeyCode.LCONTROL, VirtualKeyCode.LSHIFT },
                        VirtualKeyCode.VK_T);
                    break;
                default:
                    keyboard.ModifiedKeyStroke(VirtualKeyCode.LCONTROL, VirtualKeyCode.VK_N);
                    break;
            }
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

        private enum NewTabShortcut
        {
            ControlT,
            ControlShiftT,
            ControlN
        }
    }
}
