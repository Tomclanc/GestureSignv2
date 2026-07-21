using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Applications
{
    public class ApplicationManager : IApplicationManager, INotifyCollectionChanged
    {
        #region Private Variables

        // Create variable to hold the only allowed instance of this class
        private static ApplicationManager _instance;
        private List<IApplication> _applications;
        IEnumerable<IApplication> _recognizedApplication;
        private Timer _timer;
        private SystemWindow _recentClashPartyWindow;
        private DateTime _recentClashPartyWindowUtc;
        private SystemWindow _lastObservedForegroundWindow;
        private const int ClashPartyTargetGraceMilliseconds = 3000;
        private static readonly string[] BrowserExecutableAliases =
        {
            "MicrosoftEdge",
            "msedge",
            "msedge.exe",
            "msedgewebview2",
            "MicrosoftEdgeCP",
            "firefox",
            "chrome",
            "iexplore"
        };

        private static readonly string[] WindowsTerminalExecutableAliases =
        {
            "WindowsTerminal",
            "WindowsTerminal.exe",
            "wt",
            "wt.exe",
            "OpenConsole",
            "OpenConsole.exe"
        };

        private static readonly string[] WeChatExecutableAliases =
        {
            "WeChat",
            "WeChat.exe",
            "WeChatAppEx",
            "WeChatAppEx.exe"
        };
        #endregion

        #region Public Instance Properties

        public SystemWindow CaptureWindow { get; private set; }
        public IEnumerable<IApplication> RecognizedApplication { get { return _recognizedApplication; } }

        public List<IApplication> Applications
        {
            get
            {
                if (LoadingTask.IsCompleted)
                    return _applications != null ? _applications : _applications = new List<IApplication>();
                else
                    return new List<IApplication>();
            }
        }

        public static ApplicationManager Instance
        {
            get { return _instance ?? (_instance = new ApplicationManager()); }
        }

        public Task LoadingTask { get; }

        #endregion

        #region Constructors

        protected ApplicationManager()
        {
            // Load applications from disk, if file couldn't be loaded, create an empty applications list
            LoadingTask = LoadApplications();
            ObserveForegroundWindow(SystemWindow.ForegroundWindow);
        }

        #endregion

        #region Events

        protected void PointCapture_CaptureStarted(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            if (pointCapture.Mode == CaptureMode.Training) return;

            if (VersionHelper.IsWindows8OrGreater() && !VersionHelper.IsWindows10OrGreater())
            {
                IntPtr hwndCharmBar = FindWindow("NativeHWNDHost", "Charm Bar");
                var window = SystemWindow.FromPointEx(SystemWindow.DesktopWindow.Rectangle.Right - 1, 1, true, true);

                if (window != null && window.HWnd.Equals(hwndCharmBar))
                {
                    e.Cancel = false;
                    e.BlockTouchInputThreshold = 0;
                    return;
                }
            }

            CaptureWindow = ResolveCaptureWindow(pointCapture, e.FirstCapturedPoints.FirstOrDefault());
            _recognizedApplication = GetApplicationFromWindow(CaptureWindow);

            int maxThreshold = 0, maxLimitNumber = 1;

            foreach (IApplication app in _recognizedApplication)
            {
                switch (app)
                {
                    case GlobalApp a:
                        maxLimitNumber = a.LimitNumberOfFingers > maxLimitNumber ? a.LimitNumberOfFingers : maxLimitNumber;
                        if ((AppConfig.IgnoreFullScreen || AppConfig.IgnoreFullScreenVideo) && IsFullScreenWindow(e.FirstCapturedPoints.FirstOrDefault()))
                        {
                            Logging.LogMessage($"Gesture capture rejected by application filter. Reason=FullscreenIgnored, Application={a.Name}, Contacts={e.Points.Count}");
                            e.Cancel = true;
                            return;
                        }
                        break;
                    case UserApp a:
                        maxThreshold = a.BlockTouchInputThreshold > maxThreshold ? a.BlockTouchInputThreshold : maxThreshold;
                        maxLimitNumber = a.LimitNumberOfFingers > maxLimitNumber ? a.LimitNumberOfFingers : maxLimitNumber;
                        break;
                    case IgnoredApp a:
                        if (a.IsEnabled)
                        {
                            Logging.LogMessage($"Gesture capture rejected by application filter. Reason=IgnoredApplication, Application={a.Name}, Contacts={e.Points.Count}, Window={CaptureWindow?.HWnd}");
                            e.Cancel = true;
                            return;
                        }
                        break;
                    default:
                        return;
                }
            }
            e.Cancel = (pointCapture.SourceDevice & Devices.TouchDevice) != 0 && (e.Points.Count < maxLimitNumber);
            if (e.Cancel)
                Logging.LogMessage($"Gesture capture rejected by application filter. Reason=FingerLimit, Contacts={e.Points.Count}, Required={maxLimitNumber}, Applications={string.Join(",", _recognizedApplication.Select(app => app.Name))}");
            e.BlockTouchInputThreshold = maxThreshold;
        }

        protected void PointCapture_BeforePointsCaptured(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = (IPointCapture)sender;
            if (pointCapture.SourceDevice == Devices.TouchPad)
            {
                CaptureWindow = ResolveCaptureWindow(pointCapture, e.FirstCapturedPoints.FirstOrDefault());
                _recognizedApplication = GetApplicationFromWindow(CaptureWindow);
                return;
            }

            var appsToMatch = Applications.Where(a => a is UserApp && a.MatchActivated);
            if (appsToMatch.Any())
            {
                CaptureWindow = SystemWindow.ForegroundWindow;
                string className, title, fileName;
                GetWindowInfo(CaptureWindow, out className, out title, out fileName);
                var matchedForegroundApps = FindMatchApplications(appsToMatch, className, title, fileName);

                if (matchedForegroundApps.Length != 0)
                {
                    _recognizedApplication = matchedForegroundApps;
                    return;
                }
            }

            // Derive capture window from capture point
            CaptureWindow = ResolveCaptureWindow(pointCapture, e.FirstCapturedPoints.FirstOrDefault());
            _recognizedApplication = GetApplicationFromWindow(CaptureWindow);
            if (RecognizedOnlyGlobal(_recognizedApplication))
            {
                var foregroundWindow = SystemWindow.ForegroundWindow;
                var foregroundApplications = GetApplicationFromWindow(foregroundWindow, true)
                    .Where(app => !(app is GlobalApp))
                    .ToArray();
                if (foregroundApplications.Length != 0)
                {
                    CaptureWindow = foregroundWindow;
                    _recognizedApplication = foregroundApplications;
                }
            }
        }

        #endregion

        #region Custom Events

        public event NotifyCollectionChangedEventHandler CollectionChanged;
        public static event EventHandler ApplicationSaved;
        public static event EventHandler OnLoadApplicationsCompleted;

        #endregion

        #region Public Methods

        public void Load(IPointCapture pointCapture)
        {
            // Shortcut method to control singleton instantiation
            // Consume Point Capture events
            if (pointCapture != null)
            {
                pointCapture.CaptureStarted += new PointsCapturedEventHandler(PointCapture_CaptureStarted);
                pointCapture.BeforePointsCaptured += new PointsCapturedEventHandler(PointCapture_BeforePointsCaptured);
            }
        }

        public void AddApplication(IApplication application)
        {
            Applications.Add(application);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, application));
        }

        public void AddApplicationRange(List<IApplication> applications)
        {
            Applications.AddRange(applications);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, applications));
        }

        public void RemoveApplication(IApplication application)
        {
            Applications.Remove(application);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Remove, application));
        }

        public void ReplaceApplication(IApplication oldApplication, IApplication newApplication)
        {
            Applications.Remove(oldApplication);
            Applications.Add(newApplication);
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Replace, newApplication, oldApplication));
        }

        public void RemoveAllApplication()
        {
            Applications.Clear();
            CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }

        public void RemoveIgnoredApplications(string applicationName)
        {
            Applications.RemoveAll(app => app is IgnoredApp && app.Name == applicationName);
        }

        public bool SaveApplications()
        {
            TrimActions(Applications);

            if (_timer == null)
            {
                _timer = new Timer(new TimerCallback(SaveFile), true, 200, Timeout.Infinite);
            }
            else _timer.Change(200, Timeout.Infinite);
            return true;
        }

        private void SaveFile(object state)
        {
            // Save application list
            bool flag = FileManager.SaveObject(
                 Applications, Path.Combine(AppConfig.ApplicationDataPath, Constants.ActionFileName), true);
            if (flag) { ApplicationSaved.Invoke(this, EventArgs.Empty); }

        }

        public Task LoadApplications()
        {
            Action<bool> loadCompleted =
                result =>
                {
                    if (!result)
                        if (!LoadBackup())
                            if (!LoadLegacy())
                                if (!LoadDefaults())
                                    _applications = new List<IApplication>();
                    NormalizeBuiltInApplications();
                    OnLoadApplicationsCompleted?.Invoke(this, EventArgs.Empty);
                };

            return Task.Run(() =>
            {
                // Load application list from file
                _applications =
                    FileManager.LoadObject<List<IApplication>>(
                        Path.Combine(AppConfig.ApplicationDataPath, Constants.ActionFileName), true, true);
                return _applications != null;
            }).ContinueWith(antecendent => loadCompleted(antecendent.Result));
        }

        private bool LoadDefaults()
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Defaults", Constants.ActionFileName);

            _applications = FileManager.LoadObject<List<IApplication>>(path, false, true);
            // Ensure we got an object back
            if (_applications == null)
                return false; // No object, failed

            return true; // Success
        }

        private bool LoadBackup()
        {
            var directory = new DirectoryInfo(AppConfig.BackupPath);
            if (directory.Exists)
            {
                var actionfiles = directory.EnumerateFiles("*" + Constants.ActionExtension).OrderByDescending(f => f.LastWriteTime);
                foreach (var file in actionfiles)
                {
                    _applications = FileManager.LoadObject<List<IApplication>>(file.FullName, false, true);
                    if (_applications != null) return true;
                }
            }
            return false;
        }

        public SystemWindow GetWindowFromPoint(Point point)
        {
            var pointWindow = SystemWindow.FromPointEx(point.X, point.Y, true, true);
            if (!IsDesktopShellSurface(pointWindow))
                return pointWindow;

            var topLevelWindow = FindVisibleTopLevelWindowAtPoint(point);
            if (topLevelWindow != null)
            {
                Logging.LogMessage($"Window hit-test corrected. Reason=VisibleTopLevelOverDesktop, PointHwnd={pointWindow?.HWnd}, TargetHwnd={topLevelWindow.HWnd}");
                return topLevelWindow;
            }

            return pointWindow;
        }

        public void ObserveForegroundWindow(SystemWindow window)
        {
            if (window == null || window.HWnd == IntPtr.Zero)
                return;

            // Electron applications may hide their browser window as soon as a
            // global mouse gesture begins. Preserve the previous Clash Party HWND
            // when WinEvent reports that the foreground changed to the shell.
            if (IsDesktopShellSurface(window) && IsClashPartyWindow(_lastObservedForegroundWindow))
            {
                _recentClashPartyWindow = _lastObservedForegroundWindow;
                _recentClashPartyWindowUtc = DateTime.UtcNow;
                Logging.LogMessage($"Clash Party foreground target cached. Hwnd={_recentClashPartyWindow.HWnd}, Reason=ForegroundChangedToShell");
            }

            _lastObservedForegroundWindow = window;
        }

        private static SystemWindow FindVisibleTopLevelWindowAtPoint(Point point)
        {
            var currentProcessId = Process.GetCurrentProcess().Id;
            foreach (var window in SystemWindow.AllToplevelWindows)
            {
                try
                {
                    if (window == null ||
                        window.HWnd == IntPtr.Zero ||
                        !window.Visible ||
                        window.ProcessId == currentProcessId ||
                        IsShellHitTestSurface(window) ||
                        !GetPhysicalWindowRectangle(window).Contains(point))
                    {
                        continue;
                    }

                    return window;
                }
                catch
                {
                    // A protected or disappearing top-level window should not abort
                    // hit testing for the remaining windows.
                }
            }

            return null;
        }

        private static Rectangle GetPhysicalWindowRectangle(SystemWindow window)
        {
            const int dwmwaExtendedFrameBounds = 9;
            if (DwmGetWindowAttribute(window.HWnd, dwmwaExtendedFrameBounds, out DwmRect rect, Marshal.SizeOf(typeof(DwmRect))) == 0 &&
                rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                return Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
            }

            return window.Rectangle.ToRectangle();
        }

        private static bool IsShellHitTestSurface(SystemWindow window)
        {
            if (window == null)
                return false;

            try
            {
                var className = window.ClassName;
                return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public IApplication[] GetApplicationFromWindow(SystemWindow window, bool userApplicationOnly = false)
        {
            if (Applications == null || window == null)
            {
                return new[] { GetGlobalApplication() };
            }

            string className, title, fileName;
            GetWindowInfo(window, out className, out title, out fileName);

            IApplication[] definedApplications = userApplicationOnly
                ? FindMatchApplications(Applications.Where(a => a is UserApp), className, title, fileName)
                : FindMatchApplications(Applications.Where(a => !(a is GlobalApp)), className, title, fileName);
            // Try to find any user or ignored applications that match the given system window
            // If not user or ignored application could be found, return the global application
            return definedApplications.Length != 0
                ? definedApplications
                : userApplicationOnly ? new IApplication[0] : new IApplication[] { GetGlobalApplication() };
        }

        public IEnumerable<IApplication> GetApplicationFromPoint(Point testPoint)
        {
            var systemWindow = GetWindowFromPoint(testPoint);
            return GetApplicationFromWindow(systemWindow);
        }

        public IEnumerable<IAction> GetRecognizedDefinedAction(string GestureName)
        {
            return GetDefinedAction(GestureName, _recognizedApplication, true);
        }

        public List<IAction> GetRecognizedDefinedAction(Func<IAction, bool> predicate)
        {
            if (_recognizedApplication == null)
            {
                return new List<IAction>();
            }
            var recognizedActions = _recognizedApplication.Where(app => !(app is IgnoredApp) && app.Actions != null).SelectMany(app => app.Actions).Where(a => IsActionExecutable(a) && predicate(a)).ToList();
            // If there is was no action found on given application, try to get an action for global application
            if (recognizedActions.Count == 0)
                recognizedActions = GetGlobalApplication().Actions.Where(a => IsActionExecutable(a) && predicate(a)).ToList();

            return recognizedActions;
        }

        public IEnumerable<IAction> GetDefinedAction(string gestureName, IEnumerable<IApplication> application, bool useGlobal)
        {
            if (application == null)
            {
                return Enumerable.Empty<IAction>();
            }
            // Attempt to retrieve an action on the application passed in
            var recognizedApplications = application.ToList();
            var finalAction =
                recognizedApplications.Where(app => !(app is IgnoredApp) && app.Actions != null).SelectMany(app => app.Actions.Where(a => IsActionExecutable(a) && GestureNameEquals(a.GestureName, gestureName) && MatchesGestureHotkey(a))).ToList();
            // If there is was no action found on given application, try to get an action for global application
            if (finalAction.Count == 0 && useGlobal && !HasDefinedGestureAction(recognizedApplications, gestureName))
                finalAction = GetGlobalApplication().Actions.Where(a => IsActionExecutable(a) && GestureNameEquals(a.GestureName, gestureName) && MatchesGestureHotkey(a)).ToList();

            Logging.LogMessage($"Gesture action lookup context. Gesture={gestureName}, Applications={DescribeApplications(recognizedApplications)}, Actions={finalAction.Count}");
            // Return whatever the result was
            return finalAction;
        }

        private static bool IsActionExecutable(IAction action)
        {
            return action != null &&
                   action.IsEnabled &&
                   action.Commands != null &&
                   action.Commands.Any(command => command != null && command.IsEnabled);
        }

        private static bool HasDefinedGestureAction(IEnumerable<IApplication> applications, string gestureName)
        {
            return applications.Any(app =>
                !(app is IgnoredApp) &&
                !(app is GlobalApp) &&
                app.Actions != null &&
                app.Actions.Any(action => action != null &&
                                          IsActionExecutable(action) &&
                                          GestureNameEquals(action.GestureName, gestureName) &&
                                          MatchesGestureHotkey(action)));
        }

        private static bool GestureNameEquals(string actionGestureName, string gestureName)
        {
            return string.Equals(actionGestureName, gestureName, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesGestureHotkey(IAction action)
        {
            if (action == null || action.Hotkey == null || action.Hotkey.KeyCode == 0)
                return true;

            if (action.Hotkey.ModifierKeys != 0 && !IsModifierKeysDown(action.Hotkey.ModifierKeys))
                return false;

            return IsKeyDown(action.Hotkey.KeyCode);
        }

        private static bool IsModifierKeysDown(int modifierKeys)
        {
            return (!HasModifier(modifierKeys, 1) || IsKeyDown(18))
                && (!HasModifier(modifierKeys, 2) || IsKeyDown(17))
                && (!HasModifier(modifierKeys, 4) || IsKeyDown(16))
                && (!HasModifier(modifierKeys, 8) || IsKeyDown(91) || IsKeyDown(92));
        }

        private static bool HasModifier(int modifierKeys, int modifier)
        {
            return (modifierKeys & modifier) == modifier;
        }

        private static bool IsKeyDown(int keyCode)
        {
            return (GetAsyncKeyState(keyCode) & 0x8000) != 0;
        }

        public IApplication GetExistingUserApplication(string ApplicationName)
        {
            return Applications.FirstOrDefault(a => a is UserApp && a.Name == ApplicationName.Trim());
        }

        public bool IsGlobalAction(string ActionName)
        {
            return Applications.Exists(a => a is GlobalApp && a.Actions.Any(ac => ac.Name == ActionName.Trim()));
        }

        public bool ApplicationExists(string ApplicationName)
        {
            return Applications.Exists(a => a.Name == ApplicationName.Trim());
        }

        public IApplication[] GetAvailableUserApplications()
        {
            return Applications.Where(a => a is UserApp).OrderBy(a => a.Name).ToArray();
        }

        public IEnumerable<IgnoredApp> GetIgnoredApplications()
        {
            return Applications.Where(a => a is IgnoredApp).OrderBy(a => a.Name).Cast<IgnoredApp>();
        }

        public IApplication GetGlobalApplication()
        {
            var apps = Applications;
            GlobalApp globalApp = apps.FirstOrDefault(a => a is GlobalApp) as GlobalApp;
            if (globalApp == null)
            {
                globalApp = new GlobalApp() { Group = String.Empty };
                apps.Add(globalApp);
                return globalApp;
            }
            else return globalApp;
        }

        public IApplication[] FindMatchApplications<TApplication>(MatchUsing matchUsing, string matchString, string excludedApplication = null) where TApplication : IApplication
        {
            return Applications.FindAll(
                    a => a is TApplication &&
                        matchString.Equals(a.MatchString, StringComparison.CurrentCultureIgnoreCase) &&
                        matchUsing == a.MatchUsing &&
                        excludedApplication != a.Name).ToArray();
        }

        public SystemWindow GetForegroundApplications()
        {
            CaptureWindow = SystemWindow.ForegroundWindow;
            _recognizedApplication = GetApplicationFromWindow(CaptureWindow);
            return CaptureWindow;
        }

        public static string GetNextCommandName(string name, IAction action, int number = 1)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));

            var newName = number == 1 ? name : $"{name}({number})";
            if (action.Commands.Any(a => a.Name == newName))
                return GetNextCommandName(name, action, ++number);
            return newName;
        }

        public IApplication AddApplication<TApp>(TApp app, string executablefilePath) where TApp : IApplication
        {
            var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(executablefilePath);
            app.Name = string.IsNullOrWhiteSpace(versionInfo.ProductName) ? Path.GetFileNameWithoutExtension(executablefilePath) : versionInfo.ProductName;
            app.MatchUsing = MatchUsing.ExecutableFilename;
            app.MatchString = Path.GetFileName(executablefilePath);

            var matchApplications = FindMatchApplications<TApp>(app.MatchUsing, app.MatchString);
            if (matchApplications.Length != 0)
            {
                return matchApplications[0];
            }
            var existingApp = Applications.Find(a => a.Name == app.Name && a is TApp);
            if (existingApp != null)
            {
                return existingApp;
            }
            AddApplication(app);
            SaveApplications();
            return app;
        }

        public static SystemWindow GetRealWindow(SystemWindow window)
        {
            try
            {
                if (VersionHelper.IsWindows10OrGreater() && "ApplicationFrameWindow".Equals(window.ClassName))
                {
                    var realWindow = window.AllDescendantWindows.FirstOrDefault(w => "Windows.UI.Core.CoreWindow".Equals(w.ClassName));
                    if (realWindow != null)
                        return realWindow;

                    realWindow = window.AllDescendantWindows
                        .FirstOrDefault(w => !IsApplicationFrameHostExecutable(GetWindowExecutableFileName(w)));
                    if (realWindow != null)
                        return realWindow;
                }
                return window;
            }
            catch (Exception)
            {
                return window;
            }
        }

        public static SystemWindow GetWindowInfo(SystemWindow window, out string className, out string title, out string fileName)
        {
            var realWindow = GetRealWindow(window);
            className = fileName = null;

            if (VersionHelper.OsVersion >= new Version(10, 0, 17134))
            {
                title = window.Title;
            }
            else
                title = realWindow.Title;

            try
            {
                className = realWindow.ClassName;
            }
            catch { }

            try
            {
                fileName = GetWindowExecutableFileName(realWindow);
                if (string.IsNullOrWhiteSpace(fileName) && !ReferenceEquals(realWindow, window))
                    fileName = GetWindowExecutableFileName(window);
            }
            catch { }
            return realWindow;
        }

        internal static string GetWindowExecutableFileName(SystemWindow window)
        {
            if (window == null)
                return string.Empty;

            if (IsExplorerWindow(window))
                return "explorer.exe";

            try
            {
                var filePath = window.GetProcessFilePath();
                if (!string.IsNullOrWhiteSpace(filePath))
                    return Path.GetFileName(filePath);
            }
            catch { }

            try
            {
                var processName = window.Process?.ProcessName;
                if (!string.IsNullOrWhiteSpace(processName))
                    return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        ? processName
                        : processName + ".exe";
            }
            catch { }

            return string.Empty;
        }

        private static bool IsExplorerWindow(SystemWindow window)
        {
            try
            {
                var className = window.ClassName;
                if (IsExplorerWindowClass(className))
                    return true;

                return window.AllDescendantWindows.Any(child => IsExplorerWindowClass(child.ClassName));
            }
            catch
            {
                return false;
            }
        }

        private static bool IsExplorerWindowClass(string className)
        {
            return string.Equals(className, "CabinetWClass", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "ExploreWClass", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);
        }

        private SystemWindow ResolveCaptureWindow(IPointCapture pointCapture, Point capturePoint)
        {
            var pointWindow = GetWindowFromPoint(capturePoint);

            // Touchpad gestures target the active application; the cursor can be
            // parked over an unrelated taskbar or tray surface.
            if (pointCapture.SourceDevice == Devices.TouchPad)
                return SystemWindow.ForegroundWindow ?? pointWindow;

            // Clash Party can expose a click-through/elevated Electron surface to a
            // normal mouse-hook process, making WindowFromPoint report Progman even
            // while Clash Party is the active window. Keep this fallback deliberately
            // narrow so drawing on the desktop cannot close an arbitrary foreground app.
            if (pointCapture.SourceDevice == Devices.Mouse &&
                IsDesktopShellSurface(pointWindow))
            {
                var foregroundWindow = SystemWindow.ForegroundWindow;
                if (IsClashPartyWindow(foregroundWindow))
                {
                    _recentClashPartyWindow = foregroundWindow;
                    _recentClashPartyWindowUtc = DateTime.UtcNow;
                    Logging.LogMessage($"Gesture capture target corrected. Reason=ClashPartyShellHit, PointHwnd={pointWindow?.HWnd}, ForegroundHwnd={foregroundWindow.HWnd}");
                    return foregroundWindow;
                }

                var recentAge = DateTime.UtcNow - _recentClashPartyWindowUtc;
                if (_recentClashPartyWindow != null &&
                    recentAge.TotalMilliseconds >= 0 &&
                    recentAge.TotalMilliseconds <= ClashPartyTargetGraceMilliseconds &&
                    IsClashPartyWindow(_recentClashPartyWindow))
                {
                    Logging.LogMessage($"Gesture capture target corrected. Reason=ClashPartyRecentShellHit, PointHwnd={pointWindow?.HWnd}, CachedHwnd={_recentClashPartyWindow.HWnd}, AgeMs={recentAge.TotalMilliseconds:0}");
                    return _recentClashPartyWindow;
                }

                if (recentAge.TotalMilliseconds > ClashPartyTargetGraceMilliseconds)
                    _recentClashPartyWindow = null;
            }

            return pointWindow;
        }

        private static bool IsDesktopShellSurface(SystemWindow window)
        {
            if (window == null)
                return false;

            try
            {
                var className = window.ClassName;
                return string.Equals(className, "Progman", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "WorkerW", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(className, "SHELLDLL_DefView", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsClashPartyWindow(SystemWindow window)
        {
            if (window == null || window.HWnd == IntPtr.Zero)
                return false;

            try
            {
                var processName = window.Process?.ProcessName;
                if (string.Equals(processName, "Clash Party", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch { }

            try
            {
                return string.Equals(window.Title?.Trim(), "Clash Party", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        #endregion

        #region Private Methods

        private IApplication[] FindMatchApplications(IEnumerable<IApplication> applications, string className, string title, string fileName)
        {
            var byFileName = new List<IApplication>();
            var byTitle = new List<IApplication>();
            var byClass = new List<IApplication>();
            foreach (var app in applications)
            {
                if (!app.IsEnabled)
                    continue;

                switch (app.MatchUsing)
                {
                    case MatchUsing.WindowClass:
                        byClass.Add(app);
                        break;
                    case (MatchUsing)3:
                        byClass.Add(app);
                        break;
                    case MatchUsing.WindowTitle:
                        byTitle.Add(app);
                        break;
                    case MatchUsing.ExecutableFilename:
                        byFileName.Add(app);
                        break;
                    case MatchUsing.All:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
            List<IApplication> result = new List<IApplication>();
            if (byClass.Count != 0)
            {
                try
                {
                    result.AddRange(byClass.Where(a => a.MatchString != null && CompareString(a.MatchString, className, a.IsRegEx)));
                }
                catch
                {
                    // ignored
                }
            }
            if (byTitle.Count != 0)
            {
                try
                {
                    result.AddRange(byTitle.Where(a => a.MatchString != null && CompareString(a.MatchString, title, a.IsRegEx)));
                }
                catch
                {
                    // ignored
                }
            }
            if (byFileName.Count != 0)
            {
                try
                {
                    result.AddRange(byFileName.Where(a => a.MatchString != null && CompareExecutableFileName(a.MatchString, fileName, a.IsRegEx)));
                }
                catch
                {
                    // ignored
                }
            }
            return result.ToArray();
        }

        private static bool CompareString(string compareMatchString, string windowMatchString, bool useRegEx)
        {
            if (string.IsNullOrWhiteSpace(compareMatchString)) return false;
            if (string.IsNullOrEmpty(windowMatchString)) return false;
            return useRegEx
                ? Regex.IsMatch(windowMatchString, compareMatchString, RegexOptions.Singleline | RegexOptions.IgnoreCase)
                : string.Equals(windowMatchString.Trim(), compareMatchString.Trim(), StringComparison.CurrentCultureIgnoreCase);
        }

        private static bool RecognizedOnlyGlobal(IEnumerable<IApplication> applications)
        {
            return applications == null || !applications.Any(app => !(app is GlobalApp));
        }

        private static string DescribeApplications(IEnumerable<IApplication> applications)
        {
            if (applications == null)
                return "(null)";

            var names = applications.Select(app => $"{app.Name ?? app.GetType().Name}[{app.MatchUsing}:{app.MatchString}]").ToArray();
            return names.Length == 0 ? "(empty)" : string.Join(",", names);
        }

        private void NormalizeBuiltInApplications()
        {
            if (_applications == null)
                return;

            foreach (var app in _applications.Where(IsBrowserApplication))
                EnsureBrowserExecutableAliases(app);

            foreach (var app in _applications.Where(IsWeChatApplication))
                EnsureExecutableAliases(app, WeChatExecutableAliases);
        }

        private static bool IsBrowserApplication(IApplication app)
        {
            var matchString = app.MatchString ?? string.Empty;
            var name = app.Name ?? string.Empty;
            return app is UserApp &&
                (string.Equals(name, "浏览器", StringComparison.OrdinalIgnoreCase) ||
                 name.StartsWith("Browsers(", StringComparison.OrdinalIgnoreCase) ||
                 matchString.IndexOf("firefox", StringComparison.OrdinalIgnoreCase) >= 0 &&
                 matchString.IndexOf("chrome", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void EnsureBrowserExecutableAliases(IApplication app)
            => EnsureExecutableAliases(app, BrowserExecutableAliases);

        private static bool IsWeChatApplication(IApplication app)
        {
            if (!(app is UserApp))
                return false;

            var matchString = app.MatchString ?? string.Empty;
            var name = app.Name ?? string.Empty;
            return name.IndexOf("微信", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   matchString.IndexOf("WeChat", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   matchString.IndexOf("WeChatAppEx", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureExecutableAliases(IApplication app, IEnumerable<string> requiredAliases)
        {
            var aliases = SplitExecutableMatchCandidates(app.MatchString).ToList();
            var aliasSet = new HashSet<string>(aliases, StringComparer.OrdinalIgnoreCase);
            foreach (var alias in requiredAliases)
            {
                if (aliasSet.Contains(alias))
                    continue;

                aliases.Add(alias);
                aliasSet.Add(alias);
            }

            app.MatchString = string.Join("|", aliases);
            app.IsRegEx = true;
        }

        internal static bool CompareExecutableFileName(string compareMatchString, string windowFileName, bool useRegEx)
        {
            if (string.IsNullOrWhiteSpace(compareMatchString)) return false;
            if (string.IsNullOrWhiteSpace(windowFileName)) return false;

            var fileName = Path.GetFileName(windowFileName.Trim());
            var baseName = Path.GetFileNameWithoutExtension(fileName);

            if (useRegEx)
            {
                return Regex.IsMatch(fileName, compareMatchString, RegexOptions.Singleline | RegexOptions.IgnoreCase)
                    || (!string.IsNullOrEmpty(baseName) && Regex.IsMatch(baseName, compareMatchString, RegexOptions.Singleline | RegexOptions.IgnoreCase));
            }

            foreach (var candidate in SplitExecutableMatchCandidates(compareMatchString))
            {
                if (IsWindowsTerminalExecutable(candidate) && IsWindowsTerminalExecutable(fileName))
                    return true;

                if (string.Equals(fileName, candidate, StringComparison.CurrentCultureIgnoreCase))
                    return true;

                if (!string.IsNullOrEmpty(baseName) && string.Equals(baseName, candidate, StringComparison.CurrentCultureIgnoreCase))
                    return true;

                if (!candidate.EndsWith(".exe", StringComparison.CurrentCultureIgnoreCase) &&
                    string.Equals(fileName, candidate + ".exe", StringComparison.CurrentCultureIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool IsApplicationFrameHostExecutable(string fileName)
        {
            return string.Equals(fileName, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsWindowsTerminalExecutable(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return false;

            var normalized = Path.GetFileName(fileName.Trim());
            return WindowsTerminalExecutableAliases.Any(alias =>
                string.Equals(normalized, alias, StringComparison.OrdinalIgnoreCase));
        }

        private static IEnumerable<string> SplitExecutableMatchCandidates(string compareMatchString)
        {
            return (compareMatchString ?? string.Empty)
                .Split(new[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(candidate => candidate.Trim())
                .Where(candidate => candidate.Length != 0);
        }

#pragma warning disable CS0618
        private bool LoadLegacy()
        {
            var legacyApps = FileManager.LoadObject<List<LegacyApplicationBase>>(Path.Combine(AppConfig.ApplicationDataPath, "Actions.act"), true, true);
            if (legacyApps == null) return false;
            _applications = new List<IApplication>();
            foreach (var app in legacyApps)
            {
                var legacyUserApp = app as UserApplication;
                if (legacyUserApp != null)
                {
                    var newApp = new UserApp()
                    {
                        Actions = ConvertLegacyActions(legacyUserApp.Actions),
                        BlockTouchInputThreshold = legacyUserApp.BlockTouchInputThreshold,
                        LimitNumberOfFingers = legacyUserApp.LimitNumberOfFingers,
                        Group = legacyUserApp.Group,
                        IsRegEx = legacyUserApp.IsRegEx,
                        MatchString = legacyUserApp.MatchString,
                        MatchUsing = legacyUserApp.MatchUsing,
                        Name = legacyUserApp.Name
                    };
                    _applications.Add(newApp);
                    continue;
                }

                var legacyIgnoredApp = app as IgnoredApplication;
                if (legacyIgnoredApp != null)
                {
                    var temp = legacyIgnoredApp.Name.Split(new[] { '$' }, StringSplitOptions.RemoveEmptyEntries);
                    var newName = temp.Length > 1 ? temp[1] : legacyIgnoredApp.Name;
                    var newApp = new IgnoredApp(newName, legacyIgnoredApp.MatchUsing, legacyIgnoredApp.MatchString, legacyIgnoredApp.IsRegEx, legacyIgnoredApp.IsEnabled);
                    _applications.Add(newApp);
                    continue;
                }

                var legacyGlobalApp = app as GlobalApplication;
                if (legacyGlobalApp != null)
                {
                    IApplication newApp = new GlobalApp()
                    {
                        Actions = ConvertLegacyActions(legacyGlobalApp.Actions)
                    };
                    _applications.Add(newApp);
                    continue;
                }
            }

            return true;
        }

        private List<IAction> ConvertLegacyActions(List<GestureSign.Applications.Action> legacyActions)
        {
            if (legacyActions == null) return null;
            List<IAction> newActions = new List<IAction>();
            foreach (var grouping in legacyActions.GroupBy(a => a.GestureName))
            {
                IAction newAction = new Action()
                {
                    ActivateWindow = grouping.First().ActivateWindow,
                    Condition = grouping.First().Condition,
                    GestureName = grouping.Key,
                    IsEnabled = grouping.Any(legacyAction => legacyAction.IsEnabled),
                    Name = grouping.First().Name,
                };
                foreach (var legacyAction in grouping)
                {
                    newAction.AddCommand(new Command
                    {
                        CommandSettings = legacyAction.ActionSettings,
                        IsEnabled = legacyAction.IsEnabled,
                        Name = legacyAction.Name,
                        PluginClass = legacyAction.PluginClass,
                        PluginFilename = legacyAction.PluginFilename
                    });
                }
                newActions.Add(newAction);
            }
            return newActions;
        }
#pragma warning restore CS0618  

        private bool IsFullScreenWindow(Point targetPoint)
        {
            SystemWindow deskWindow = SystemWindow.DesktopWindow;

            SystemWindow sw = SystemWindow.FromPoint(targetPoint.X, targetPoint.Y);
            if (sw == null) return false;

            // get the window with the largest area
            RECT rect = sw.Rectangle;
            int area = rect.Height * rect.Width;
            while (sw.ParentSymmetric != null)
            {
                sw = sw.ParentSymmetric;
                RECT parentRect = sw.Rectangle;
                int parentArea = parentRect.Height * parentRect.Width;
                if (parentArea > area)
                {
                    area = parentArea;
                    rect = parentRect;
                }
            }

            if (sw.HWnd == IntPtr.Zero || sw == deskWindow || sw == SystemWindow.ShellWindow)
                return false;

            var desktopRect = deskWindow.Rectangle;

            if (rect.Left == desktopRect.Left && rect.Top == desktopRect.Top && rect.Right == desktopRect.Right && rect.Bottom == desktopRect.Bottom)
            {
                switch (sw.ClassName)
                {
                    case "WorkerW":
                    case "Progman":
                    case "CanvasWindow":
                    case "ImmersiveLauncher":
                        return false;
                    default:
                        return true;
                }
            }

            return false;
        }

        private void TrimActions(IEnumerable<IApplication> applications)
        {
            foreach (var app in applications)
            {
                if (app.Actions == null) continue;
                var emptyActions = app.Actions.Where(a => a.Commands == null || !a.Commands.Any()).ToList();
                emptyActions.ForEach(a => app.RemoveAction(a));
            }
        }

        #endregion

        #region P/Invoke
        [DllImport("user32.dll", EntryPoint = "FindWindow")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out DwmRect value, int valueSize);

        [StructLayout(LayoutKind.Sequential)]
        private struct DwmRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion
    }
}
