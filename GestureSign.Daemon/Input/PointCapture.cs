using GestureSign.Common;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Daemon.Filtration;
using GestureSign.Daemon.Surface;
using GestureSign.PointPatterns;
using ManagedWinapi.Hooks;
using ManagedWinapi.Windows;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;

namespace GestureSign.Daemon.Input
{
    public class PointCapture : ILoadable, IPointCapture, IDisposable
    {
        #region Private Variables

        private const uint WINEVENT_OUTOFCONTEXT = 0;
        private const uint EVENT_SYSTEM_FOREGROUND = 3;
        private const uint WINEVENT_SKIPOWNPROCESS = 0x0002; // Don't call back for events on installer's process
        private const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
        private const uint WM_NCHITTEST = 0x0084;
        private const uint GA_ROOT = 2;
        private const uint SMTO_BLOCK = 0x0001;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const int HTMINBUTTON = 8;
        private const int HTMAXBUTTON = 9;
        private const int HTCLOSE = 20;
        private const int GWL_STYLE = -16;
        private const int SM_CXSIZE = 30;
        private const int SM_CYSIZE = 31;
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;

        // Create new Touch hook control to capture global input from Touch, and create an event translator to get formal events
        private readonly PointEventTranslator _pointEventTranslator;
        private readonly InputProvider _inputProvider;
        private readonly PointerInputTargetWindow _pointerInputTargetWindow;
        private readonly List<IPointPattern> _pointPatternCache = new List<IPointPattern>();
        private readonly HashSet<IntPtr> _touchScreenPassthroughWindows = new HashSet<IntPtr>();
        private readonly object _touchScreenPassthroughWindowLock = new object();
        private readonly System.Threading.Timer _blockTouchDelayTimer;
        private readonly System.Threading.Timer _touchScreenPassthroughReleaseTimer;
        private SurfaceForm _surfaceForm;

        private System.Threading.Timer _initialTimeoutTimer;
        SynchronizationContext _currentContext;

        private Dictionary<int, List<Point>> _pointsCaptured;
        private List<int> _touchScreenContactOrder;
        private Dictionary<int, Point> _touchScreenUpPoints;
        private bool _touchScreenBlockedUntilRelease;
        private volatile bool _touchScreenPassthroughActive;
        private int _requiredContactCount = 1;
        // Create variable to hold the only allowed instance of this class
        static readonly PointCapture _Instance = new PointCapture();

        private CaptureMode _mode = CaptureMode.Normal;
        private volatile CaptureState _state;

        delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        readonly WinEventDelegate _winEventDele;
        private readonly IntPtr _hWinEventHook;
        private GCHandle _winEventGch;

        private bool disposedValue = false; // To detect redundant calls

        private int? _blockTouchInputThreshold;
        private Point _touchPadStartPoint;
        private PointF _touchPadRawVisualOrigin;
        private Dictionary<int, Point> _touchPadRawStartPoints;
        private Dictionary<int, List<Point>> _touchPadVisualPoints;
        private List<List<Point>> _lastVisualFeedbackPoints;
        private string _liveGestureHintName;
        private string _fallbackGestureName;
        private string _fallbackGestureActionName;
        private int _fallbackGesturePointCount;

        #endregion

        #region PInvoke 

        [DllImport("user32.dll")]
        static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessageTimeout(
            IntPtr hwnd,
            uint message,
            IntPtr wParam,
            IntPtr lParam,
            uint flags,
            uint timeout,
            out UIntPtr result);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetricsForDpi(int index, uint dpi);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        #endregion

        #region Public Instance Properties

        public Devices SourceDevice { get { return _pointEventTranslator.SourceDevice; } }

        public LowLevelMouseHook MouseHook
        {
            get { return _inputProvider.LowLevelMouseHook; }
        }

        public bool TemporarilyDisableCapture { get; set; }

        public List<Point>[] InputPoints
        {
            get
            {
                if (_pointsCaptured == null)
                    return new List<Point>[0];
                return _pointsCaptured.Values.ToArray();
            }
        }

        public CaptureState State
        {
            get { return _state; }
            set { _state = value; }
        }

        public CaptureMode Mode
        {
            get { return _mode; }
            set
            {
                if (value == _mode) return;
                _mode = value;
                OnModeChanged(new ModeChangedEventArgs(value));
            }
        }

        #endregion

        #region Custom Events

        public event ApplicationChangedEventHandler ForegroundApplicationsChanged;
        // Create an event to notify subscribers that CaptureState has been changed
        public event ModeChangedEventHandler ModeChanged;

        protected virtual void OnModeChanged(ModeChangedEventArgs e)
        {
            if (ModeChanged != null) ModeChanged(this, e);
        }

        // Create event to notify subscribers that the capture process has started
        public event PointsCapturedEventHandler CaptureStarted;

        protected virtual void OnCaptureStarted(PointsCapturedEventArgs e)
        {
            if (CaptureStarted != null) CaptureStarted(this, e);
        }

        // Create event to notify subscribers that a point set has been captured
        public event PointsCapturedEventHandler AfterPointsCaptured;
        public event PointsCapturedEventHandler BeforePointsCaptured;
        public event RecognitionEventHandler GestureRecognized;
        //public event RecognitionEventHandler GestureNotRecognized;

        protected virtual void OnAfterPointsCaptured(PointsCapturedEventArgs e)
        {
            if (AfterPointsCaptured != null) AfterPointsCaptured(this, e);
        }

        protected virtual void OnBeforePointsCaptured(PointsCapturedEventArgs e)
        {
            if (BeforePointsCaptured != null) BeforePointsCaptured(this, e);
        }

        protected virtual void OnGestureRecognized(RecognitionEventArgs e)
        {
            if (GestureRecognized != null) GestureRecognized(this, e);
        }

        //protected virtual void OnGestureNotRecognized(RecognitionEventArgs e)
        //{
        //    if (GestureNotRecognized != null) GestureNotRecognized(this, e);
        //}

        // Create event to notify subscribers that a single point has been captured
        public event PointsCapturedEventHandler PointCaptured;

        protected virtual void OnPointCaptured(PointsCapturedEventArgs e)
        {
            if (PointCaptured != null) PointCaptured(this, e);
        }

        // Create event to notify subscribers that the capture process has ended
        public event EventHandler CaptureEnded;

        protected virtual void OnCaptureEnded()
        {
            if (CaptureEnded != null) CaptureEnded(this, new EventArgs());
        }

        // Create event to notify subscribers that the capture has been canceled
        public event PointsCapturedEventHandler CaptureCanceled;

        protected virtual void OnCaptureCanceled(PointsCapturedEventArgs e)
        {
            if (CaptureCanceled != null) CaptureCanceled(this, e);
        }

        #endregion

        #region Public Properties

        public static PointCapture Instance
        {
            get { return _Instance; }
        }

        public void RegisterTouchScreenPassthroughWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            lock (_touchScreenPassthroughWindowLock)
                _touchScreenPassthroughWindows.Add(handle);
        }

        public void UnregisterTouchScreenPassthroughWindow(IntPtr handle)
        {
            if (handle == IntPtr.Zero)
                return;

            lock (_touchScreenPassthroughWindowLock)
                _touchScreenPassthroughWindows.Remove(handle);
        }

        public void BeginTouchScreenPassthrough()
        {
            _touchScreenPassthroughReleaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
            _touchScreenPassthroughActive = true;
        }

        public void EndTouchScreenPassthrough(int delayMilliseconds = 500)
        {
            _touchScreenPassthroughReleaseTimer.Change(Math.Max(0, delayMilliseconds), Timeout.Infinite);
        }

        #endregion

        #region Constructors

        protected PointCapture()
        {
            _surfaceForm = new SurfaceForm();
            _touchScreenPassthroughReleaseTimer = new System.Threading.Timer(
                _ => _touchScreenPassthroughActive = false,
                null,
                Timeout.Infinite,
                Timeout.Infinite);

            CaptureStarted += (o, e) =>
            {
                _lastVisualFeedbackPoints = null;
                _liveGestureHintName = null;
                _surfaceForm.StartDrawing(e.FirstCapturedPoints);
            };
            CaptureEnded += (o, e) =>
            {
                _liveGestureHintName = null;
                _lastVisualFeedbackPoints = null;
                _surfaceForm.EndDrawing();
            };
            CaptureCanceled += (o, e) =>
            {
                _liveGestureHintName = null;
                _lastVisualFeedbackPoints = null;
                _surfaceForm.EndDrawing();
            };
            PointCaptured += (o, e) =>
            {
                if (State == CaptureState.Capturing || SourceDevice == Devices.TouchPad && State == CaptureState.CapturingInvalid)
                {
                    _surfaceForm.DrawPoints(e.Points);
                    _lastVisualFeedbackPoints = ClonePoints(e.Points);
                    ShowLiveGestureHintIfMatched(e.Points);
                }
            };
            PluginManager.Instance.GestureActionExecuted += PluginManager_GestureActionExecuted;

            _inputProvider = new InputProvider();
            _pointEventTranslator = new PointEventTranslator(_inputProvider);
            _pointEventTranslator.PointDown += (PointEventTranslator_PointDown);
            _pointEventTranslator.PointUp += (PointEventTranslator_PointUp);
            _pointEventTranslator.PointMove += (PointEventTranslator_PointMove);

            _currentContext = SynchronizationContext.Current;

            _winEventDele = WinEventProc;
            _winEventGch = GCHandle.Alloc(_winEventDele);
            _hWinEventHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _winEventDele, 0, 0, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);

            if (AppConfig.UiAccess)
            {
                _pointerInputTargetWindow = new PointerInputTargetWindow();
                ModeChanged += (o, e) =>
                {
                    if (e.Mode == CaptureMode.UserDisabled)
                        _pointerInputTargetWindow.BlockTouchInputThreshold = 0;
                };
                _blockTouchDelayTimer = new System.Threading.Timer(UpdateBlockTouchInputThresholdCallback, null, Timeout.Infinite, Timeout.Infinite);
                ForegroundApplicationsChanged += PointCapture_ForegroundApplicationsChanged;
            }

            SystemEvents.SessionSwitch += SystemEvents_SessionSwitch;
        }

        private void PluginManager_GestureActionExecuted(object sender, GestureActionExecutedEventArgs e)
        {
            // Action hints are shown while the gesture is still being drawn.
            // Keeping the old post-execution hint would make the label appear after release.
        }

        private void ShowLiveGestureHintIfMatched(List<List<Point>> points)
        {
            if (Mode == CaptureMode.Training || points == null || points.Count == 0)
                return;

            var gestureName = PreviewActionGestureName(points);
            if (string.IsNullOrWhiteSpace(gestureName))
            {
                ClearLiveGestureHintIfShown(points);
                return;
            }

            var action = ApplicationManager.Instance.GetRecognizedDefinedAction(gestureName)?.FirstOrDefault();
            if (action == null || string.IsNullOrWhiteSpace(action.Name))
            {
                ClearLiveGestureHintIfShown(points);
                return;
            }

            _fallbackGestureName = gestureName;
            _fallbackGestureActionName = action.Name;
            _fallbackGesturePointCount = CountGesturePoints(points);

            if (!AppConfig.ShowGestureActionHint || string.Equals(action.Name, _liveGestureHintName, StringComparison.Ordinal))
                return;

            _liveGestureHintName = action.Name;
            _surfaceForm.ShowLiveGestureHint(ClonePoints(points), action.Name);
        }

        private void ClearLiveGestureHintIfShown(List<List<Point>> points)
        {
            if (!string.IsNullOrWhiteSpace(_fallbackGestureName))
            {
                Logging.LogMessage($"Live gesture action invalidated. Gesture={_fallbackGestureName}, Action={_fallbackGestureActionName}");
            }

            _fallbackGestureName = null;
            _fallbackGestureActionName = null;
            _fallbackGesturePointCount = 0;

            var hadVisibleHint = !string.IsNullOrWhiteSpace(_liveGestureHintName);

            _liveGestureHintName = null;
            if (!hadVisibleHint)
                return;

            _surfaceForm.ClearLiveGestureHint(ClonePoints(points));
        }

        private static string PreviewActionGestureName(IEnumerable<List<Point>> points)
        {
            var pointArray = points?.Select(stroke => stroke?.ToArray() ?? Array.Empty<Point>()).ToArray();
            if (pointArray == null || pointArray.Length == 0)
                return null;

            var actions = ApplicationManager.Instance
                .GetRecognizedDefinedAction(action => !string.IsNullOrWhiteSpace(action.GestureName));
            var smartCloseGestureNames = actions
                .Where(action => action.Commands != null && action.Commands.Any(command =>
                    command != null &&
                    command.IsEnabled &&
                    !string.IsNullOrWhiteSpace(command.PluginClass) &&
                    command.PluginClass.IndexOf("SmartClose", StringComparison.OrdinalIgnoreCase) >= 0))
                .Select(action => action.GestureName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var actionGestureNames = actions
                .Select(action => action.GestureName)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // Smart Close is commonly drawn as a compact L. Give only that action a
            // modest tolerance boost; the axis-balance guard still rejects straight
            // two-finger scrolling strokes.
            return GestureManager.Instance.PreviewGestureName(pointArray, smartCloseGestureNames, 74)
                   ?? GestureManager.Instance.PreviewGestureName(pointArray, actionGestureNames)
                   ?? GestureManager.Instance.PreviewGestureName(pointArray);
        }

        private static int CountGesturePoints(IEnumerable<List<Point>> points)
        {
            return points?.Sum(stroke => stroke?.Count ?? 0) ?? 0;
        }

        private static List<List<Point>> ClonePoints(IEnumerable<List<Point>> points)
        {
            return points?.Select(stroke => stroke?.ToList() ?? new List<Point>()).ToList();
        }

        #endregion

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _initialTimeoutTimer?.Dispose();
                    _blockTouchDelayTimer?.Dispose();
                    _touchScreenPassthroughReleaseTimer?.Dispose();
                    _pointEventTranslator?.Dispose();
                    _pointerInputTargetWindow?.Dispose();
                    _inputProvider?.Dispose();
                    _surfaceForm?.Dispose();
                }
                _surfaceForm = null;

                SystemEvents.SessionSwitch -= SystemEvents_SessionSwitch;
                if (_hWinEventHook != IntPtr.Zero)
                    UnhookWinEvent(_hWinEventHook);
                if (_winEventGch.IsAllocated)
                {
                    _winEventGch.Free();
                }

                disposedValue = true;
            }
        }

        ~PointCapture()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion

        #region System Events

        private void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType == EVENT_SYSTEM_FOREGROUND || eventType == EVENT_SYSTEM_MINIMIZEEND)
            {
                if (hwnd.Equals(IntPtr.Zero))
                    return;
                var systemWindow = new SystemWindow(hwnd);
                ApplicationManager.Instance.ObserveForegroundWindow(systemWindow);
                if (State != CaptureState.Ready || Mode != CaptureMode.Normal)
                    return;
                if (!systemWindow.Visible)
                    return;
                var apps = ApplicationManager.Instance.GetApplicationFromWindow(systemWindow);
                ForegroundApplicationsChanged?.Invoke(this, new ApplicationChangedEventArgs(apps));
            }
        }

        private void SystemEvents_SessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.SessionUnlock:
                    if (State == CaptureState.Disabled)
                        State = CaptureState.Ready;
                    break;
                case SessionSwitchReason.SessionLock:
                    State = CaptureState.Disabled;
                    break;
                default:
                    break;
            }
        }

        #endregion

        #region Events

        private void PointCapture_ForegroundApplicationsChanged(object sender, ApplicationChangedEventArgs appsChanged)
        {
            if (appsChanged.Applications != null)
            {
                var userAppList = appsChanged.Applications.Where(application => application is UserApp).ToList();
                // Always update the filter. Previously, switching from a configured app
                // to an unconfigured/ignored game left the former app's threshold active,
                // so the global pointer target kept intercepting the game's touch frames.
                var threshold = userAppList.Count == 0
                    ? 0
                    : userAppList.Cast<UserApp>().Max(app => app.BlockTouchInputThreshold);
                UpdateBlockTouchInputThreshold(threshold);
            }
        }

        protected void PointEventTranslator_PointDown(object sender, InputPointsEventArgs e)
        {
            if (SourceDevice == Devices.TouchScreen)
            {
                if (_touchScreenBlockedUntilRelease)
                    return;

                // While a GestureSign-owned touch UI is open, pass every touch
                // frame through. WindowFromPoint can briefly report the taskbar
                // or desktop while an injected contact is being promoted, so a
                // handle-only exclusion is not sufficient for tray menu taps.
                if (_touchScreenPassthroughActive)
                {
                    _touchScreenBlockedUntilRelease = true;
                    _pointerInputTargetWindow?.TemporarilyDisable();
                    Logging.LogMessage("TouchScreen capture bypassed. Reason=ActivePassthroughWindow");
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                    return;
                }

                if (IsInTouchScreenPassthroughWindow(e.InputPointList, out var passthroughPoint))
                {
                    _touchScreenBlockedUntilRelease = true;
                    _pointerInputTargetWindow?.TemporarilyDisable();
                    Logging.LogMessage($"TouchScreen capture bypassed. Reason=RegisteredPassthroughWindow, Point={passthroughPoint.X},{passthroughPoint.Y}");
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                    return;
                }

                // Caption buttons must be excluded before TryBeginCapture. Waiting
                // until the edge trigger runs can already move the global pointer
                // filter into gesture-capture state and occasionally lose the
                // original minimize/maximize/close tap during reinjection.
                if (IsInSystemCaptionButtonArea(e.InputPointList, out var captionPoint))
                {
                    _touchScreenBlockedUntilRelease = true;
                    Logging.LogMessage($"TouchScreen capture bypassed. Reason=SystemCaptionButton, Point={captionPoint.X},{captionPoint.Y}");
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                    return;
                }

                if (IsInTouchScreenBlockedArea(e.InputPointList))
                {
                    _touchScreenBlockedUntilRelease = true;
                    if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
                    {
                        OnCaptureCanceled(new PointsCapturedEventArgs(
                            _pointsCaptured?.Values.Select(points => new List<Point>(points)).ToList() ?? new List<List<Point>>(),
                            _pointsCaptured?.Values.Select(points => points.FirstOrDefault()).ToList() ?? new List<Point>()));
                        State = CaptureState.Ready;
                        ResetCaptureBuffers();
                    }
                    Logging.LogMessage($"TouchScreen capture suppressed by blocked area. Contacts={e.InputPointList?.Count ?? 0}");
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                    return;
                }
            }

            if (State == CaptureState.Ready || State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.High;

                var timeout = AppConfig.InitialTimeout;
                if (timeout > 0)
                {
                    if (_initialTimeoutTimer == null)
                    {
                        _initialTimeoutTimer = new System.Threading.Timer(InitialTimeoutCallback, null, Timeout.Infinite, Timeout.Infinite);
                    }
                    _initialTimeoutTimer.Change(timeout, Timeout.Infinite);
                }

                if (SourceDevice == Devices.TouchScreen &&
                    (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid) &&
                    _pointsCaptured != null)
                {
                    MergeTouchScreenContacts(e.InputPointList);
                    var contactCountSatisfied = _pointsCaptured.Count >= _requiredContactCount;
                    e.Handled = contactCountSatisfied && Mode != CaptureMode.UserDisabled;
                    Logging.LogMessage($"TouchScreen contacts merged into active capture. Contacts={_pointsCaptured.Count}, Required={_requiredContactCount}, Ready={contactCountSatisfied}");
                    return;
                }

                // Try to begin capture process, if capture started then don't notify other applications of a Point event, otherwise do
                if (!TryBeginCapture(e.InputPointList))
                {
                    Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                }
                else e.Handled = _pointsCaptured.Count >= _requiredContactCount && Mode != CaptureMode.UserDisabled;
            }
        }

        protected void PointEventTranslator_PointMove(object sender, InputPointsEventArgs e)
        {
            if (SourceDevice == Devices.TouchScreen && _touchScreenBlockedUntilRelease)
                return;

            // Only add point if we're capturing
            if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid)
            {
                AddPoint(e.InputPointList);
            }
            UpdateBlockTouchInputThreshold();
        }

        protected void PointEventTranslator_PointUp(object sender, InputPointsEventArgs e)
        {
            if (SourceDevice == Devices.TouchScreen && _touchScreenBlockedUntilRelease)
            {
                _touchScreenBlockedUntilRelease = false;
                State = CaptureState.Ready;
                ResetCaptureBuffers();
                UpdateBlockTouchInputThreshold();
                _initialTimeoutTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
                return;
            }

            if (State == CaptureState.Capturing || State == CaptureState.CapturingInvalid && (SourceDevice & Devices.TouchDevice) != 0)
            {
                var contactCountSatisfied = SourceDevice != Devices.TouchScreen ||
                                            _pointsCaptured?.Count >= _requiredContactCount;
                e.Handled = contactCountSatisfied && Mode != CaptureMode.UserDisabled;

                if (SourceDevice == Devices.TouchScreen && e.InputPointList != null)
                    _touchScreenUpPoints = e.InputPointList
                        .GroupBy(point => point.ContactIdentifier)
                        .ToDictionary(group => group.Key, group => group.Last().Point);

                if ((SourceDevice & Devices.TouchDevice) != 0 && e.InputPointList != null)
                    AddPoint(e.InputPointList);

                if (contactCountSatisfied)
                {
                    EndCapture();
                }
                else
                {
                    Logging.LogMessage($"TouchScreen provisional capture canceled. Reason=FingerLimit, Contacts={_pointsCaptured?.Count ?? 0}, Required={_requiredContactCount}");
                    OnCaptureCanceled(new PointsCapturedEventArgs(
                        _pointsCaptured?.Values.Select(points => new List<Point>(points)).ToList() ?? new List<List<Point>>(),
                        _pointsCaptured?.Values.Select(points => points.FirstOrDefault()).ToList() ?? new List<Point>()));
                    State = CaptureState.Ready;
                    ResetCaptureBuffers();
                }

                if (TemporarilyDisableCapture && Mode == CaptureMode.UserDisabled)
                {
                    TemporarilyDisableCapture = false;
                    ToggleUserDisablePointCapture();
                }
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            }
            else if (State == CaptureState.CapturingInvalid && SourceDevice == Devices.Mouse)
            {
                if (Mode != CaptureMode.UserDisabled)
                {
                    State = CaptureState.Disabled;

                    var observeExceptionsTask = new Action<Task>(t =>
                    {
                        State = CaptureState.Ready;
                        Console.WriteLine($"{t.Exception.InnerException.GetType().Name}: {t.Exception.InnerException.Message}");
                    });

                    var clickAsync = Task.Factory.StartNew(delegate
                    {
                        InputSimulator simulator = new InputSimulator();
                        switch (AppConfig.DrawingButton)
                        {
                            case MouseActions.Left:
                                simulator.Mouse.LeftButtonClick();
                                break;
                            case MouseActions.Middle:
                                simulator.Mouse.MiddleButtonClick();
                                break;
                            case MouseActions.Right:
                                simulator.Mouse.RightButtonClick();
                                break;
                            case MouseActions.XButton1:
                                simulator.Mouse.XButtonClick(1);
                                break;
                            case MouseActions.XButton2:
                                simulator.Mouse.XButtonClick(2);
                                break;
                        }
                        State = CaptureState.Ready;
                    }).ContinueWith(observeExceptionsTask, TaskContinuationOptions.OnlyOnFaulted);

                    e.Handled = true;
                }
                else
                {
                    State = CaptureState.Ready;
                }
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            }
            else if (State == CaptureState.TriggerFired)
            {
                State = CaptureState.Ready;
                e.Handled = Mode != CaptureMode.UserDisabled;
                Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
            }

            UpdateBlockTouchInputThreshold();
            if (_initialTimeoutTimer != null)
                _initialTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        #endregion

        #region Private Methods

        private void UpdateBlockTouchInputThreshold(int? threshold = null)
        {
            if (!AppConfig.UiAccess) return;

            if (threshold != null)
                _blockTouchInputThreshold = threshold;
            if (_blockTouchInputThreshold == 0)
            {
                // Passing input through is time-sensitive: leaving the old pointer
                // target registered for the normal debounce interval can still consume
                // the first tap immediately after switching to a game.
                _blockTouchDelayTimer.Change(Timeout.Infinite, Timeout.Infinite);
                _pointerInputTargetWindow.BlockTouchInputThreshold = 0;
                _blockTouchInputThreshold = null;
                return;
            }
            if (_blockTouchInputThreshold != null)
                _blockTouchDelayTimer.Change(100, Timeout.Infinite);
        }

        private void UpdateBlockTouchInputThresholdCallback(object o)
        {
            if (!_blockTouchInputThreshold.HasValue) return;

            _currentContext.Post((state) =>
            {
                _pointerInputTargetWindow.BlockTouchInputThreshold = _blockTouchInputThreshold.GetValueOrDefault();
                _blockTouchInputThreshold = null;
            }, null);
        }

        private void InitialTimeoutCallback(object o)
        {
            _currentContext.Post((state) =>
            {
                if (State != CaptureState.CapturingInvalid) return;

                try
                {
                    if (SourceDevice == Devices.TouchScreen && _pointerInputTargetWindow != null)
                    {
                        if (_pointerInputTargetWindow.BlockTouchInputThreshold > 1)
                            _pointerInputTargetWindow.TemporarilyDisable();
                    }
                    else if (SourceDevice == Devices.Mouse)
                    {
                        InputSimulator simulator = new InputSimulator();
                        switch (AppConfig.DrawingButton)
                        {
                            case MouseActions.Left:
                                simulator.Mouse.LeftButtonDown();
                                break;
                            case MouseActions.Middle:
                                simulator.Mouse.MiddleButtonDown();
                                break;
                            case MouseActions.Right:
                                simulator.Mouse.RightButtonDown();
                                break;
                            case MouseActions.XButton1:
                                simulator.Mouse.XButtonDown(1);
                                break;
                            case MouseActions.XButton2:
                                simulator.Mouse.XButtonDown(2);
                                break;
                        }
                    }
                    State = CaptureState.Ready;
                }
                catch
                {
                    State = CaptureState.Ready;
                }
            }, null);
        }

        private bool TryBeginCapture(List<InputPoint> firstPoint)
        {
            Logging.LogMessage($"Gesture capture started. Device={SourceDevice}, Mode={Mode}, Contacts={firstPoint.Count}, DrawingButton={AppConfig.DrawingButton}");

            // Create capture args so we can notify subscribers that capture has started and allow them to cancel if they want.
            PointsCapturedEventArgs captureStartedArgs;
            if (SourceDevice == Devices.TouchPad)
            {
                _touchPadStartPoint = System.Windows.Forms.Cursor.Position;
                _touchPadRawVisualOrigin = GetTouchPadVisualOrigin(firstPoint);
                _touchPadRawStartPoints = firstPoint.ToDictionary(p => p.ContactIdentifier, p => p.Point);
                _touchPadVisualPoints = firstPoint.ToDictionary(p => p.ContactIdentifier, _ => new List<Point>(30));
                captureStartedArgs = new PointsCapturedEventArgs(firstPoint.Select(p => new List<Point>() { p.Point }).ToList(), new List<Point>() { _touchPadStartPoint });
            }
            else
            {
                captureStartedArgs = new PointsCapturedEventArgs(firstPoint.Select(p => p.Point).ToList());
            }
            OnCaptureStarted(captureStartedArgs);

            UpdateBlockTouchInputThreshold(Mode == CaptureMode.Normal ? captureStartedArgs.BlockTouchInputThreshold : 0);

            if (captureStartedArgs.Cancel)
            {
                Logging.LogMessage("Gesture capture canceled before start by subscriber.");
                return false;
            }

            State = captureStartedArgs.ForceCapture ? CaptureState.Capturing : CaptureState.CapturingInvalid;
            _requiredContactCount = Math.Max(1, captureStartedArgs.RequiredContactCount);

            _touchScreenContactOrder = SourceDevice == Devices.TouchScreen
                ? firstPoint.Select(point => point.ContactIdentifier).Distinct().ToList()
                : null;
            _touchScreenUpPoints = SourceDevice == Devices.TouchScreen
                ? new Dictionary<int, Point>()
                : null;

            // Clear old gesture from point list so we can start adding the new captures points to the list 
            _pointsCaptured = new Dictionary<int, List<Point>>(firstPoint.Count);
            _liveGestureHintName = null;
            _fallbackGestureName = null;
            _fallbackGestureActionName = null;
            _fallbackGesturePointCount = 0;
            if (AppConfig.IsOrderByLocation)
            {
                foreach (var rawData in firstPoint.OrderBy(p => p.Point.X))
                {
                    if (!_pointsCaptured.ContainsKey(rawData.ContactIdentifier))
                        _pointsCaptured.Add(rawData.ContactIdentifier, new List<Point>(30));
                }
            }
            else
            {
                foreach (var rawData in firstPoint.OrderBy(p => p.ContactIdentifier))
                {
                    if (!_pointsCaptured.ContainsKey(rawData.ContactIdentifier))
                        _pointsCaptured.Add(rawData.ContactIdentifier, new List<Point>(30));
                }
            }
            AddPoint(firstPoint);
            return true;
        }

        private void MergeTouchScreenContacts(List<InputPoint> points)
        {
            if (points == null || _pointsCaptured == null)
                return;

            _touchScreenContactOrder ??= new List<int>();
            foreach (var point in points)
            {
                if (!_touchScreenContactOrder.Contains(point.ContactIdentifier))
                    _touchScreenContactOrder.Add(point.ContactIdentifier);

                if (!_pointsCaptured.ContainsKey(point.ContactIdentifier))
                    _pointsCaptured.Add(point.ContactIdentifier, new List<Point>(30));
            }

            var pointLocations = points
                .GroupBy(point => point.ContactIdentifier)
                .ToDictionary(group => group.Key, group => group.Last().Point);
            var orderedStrokes = AppConfig.IsOrderByLocation
                ? _pointsCaptured.OrderBy(stroke => pointLocations.TryGetValue(stroke.Key, out var point) ? point.X : int.MaxValue)
                : _pointsCaptured.OrderBy(stroke => stroke.Key);
            _pointsCaptured = orderedStrokes.ToDictionary(stroke => stroke.Key, stroke => stroke.Value);

            AddPoint(points);
        }

        private void EndCapture()
        {

            // Create points capture event args, to be used to send off to event subscribers or to simulate original Point event
            PointsCapturedEventArgs pointsInformation = SourceDevice == Devices.TouchPad ?
                new PointsCapturedEventArgs(_pointsCaptured.Values.ToList(), new List<Point>() { _touchPadStartPoint }) :
                new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), _pointsCaptured.Values.Select(p => p.FirstOrDefault()).ToList());

            // Notify subscribers that capture has ended （draw end）
            OnCaptureEnded();
            State = CaptureState.Ready;

            Logging.LogMessage($"Gesture capture ended. Device={SourceDevice}, Mode={Mode}, Strokes={pointsInformation.Points.Count}, Points={pointsInformation.Points.Sum(p => p.Count)}");

            // Notify PointsCaptured event subscribers that points have been captured.
            //CaptureWindow GetGestureName
            OnBeforePointsCaptured(pointsInformation);

            if (pointsInformation.Cancel)
            {
                Logging.LogMessage("Gesture capture canceled after preprocessing.");
                ResetCaptureBuffers();
                return;
            }

            if (Mode == CaptureMode.Training && !(_pointsCaptured.Count == 1 && _pointsCaptured.Values.First().Count == 1))
            {
                _pointPatternCache.Clear();
                _pointPatternCache.Add(new PointPattern(_pointsCaptured.Values));

                if (!NamedPipe.SendMessageAsync(IpcCommands.GotGesture, Constants.Settings, _pointPatternCache.Select(p => p.Points).ToArray(), false).Result)
                    Mode = CaptureMode.Normal;

                // The pipe call has completed synchronously. Keeping the training
                // pattern here would retain the complete last recording indefinitely.
                _pointPatternCache.Clear();
            }

            // Fire recognized event if we found a gesture match, otherwise throw not recognized event
            var recognizedGestureName = ResolveActionGestureName(GestureManager.Instance.GestureName, pointsInformation.Points);
            if (recognizedGestureName != null)
            {
                List<Point> capturedPoints = SourceDevice == Devices.TouchPad ? new List<Point>() { _touchPadStartPoint } : pointsInformation.FirstCapturedPoints;
                var releasedPoints = SourceDevice == Devices.TouchScreen
                    ? _pointsCaptured.Keys.Select(identifier =>
                        _touchScreenUpPoints != null && _touchScreenUpPoints.TryGetValue(identifier, out var point)
                            ? point
                            : _pointsCaptured[identifier].LastOrDefault()).ToList()
                    : null;
                Logging.LogMessage($"Gesture recognized. Name={recognizedGestureName}, Contacts={string.Join(",", _pointsCaptured.Keys)}");
                OnGestureRecognized(new RecognitionEventArgs(
                    recognizedGestureName,
                    pointsInformation.Points,
                    capturedPoints,
                    _pointsCaptured.Keys.ToList(),
                    _touchScreenContactOrder,
                    releasedPoints));
            }
            else
            {
                Logging.LogMessage("Gesture not recognized.");
            }

            OnAfterPointsCaptured(pointsInformation);

            ResetCaptureBuffers();
        }

        private void ResetCaptureBuffers()
        {
            _pointsCaptured?.Clear();
            _touchScreenContactOrder = null;
            _touchScreenUpPoints = null;
            _requiredContactCount = 1;
            _touchPadRawStartPoints = null;
            _touchPadVisualPoints = null;
            _touchPadRawVisualOrigin = PointF.Empty;
            _lastVisualFeedbackPoints = null;
        }

        private static bool IsInTouchScreenBlockedArea(IReadOnlyCollection<InputPoint> points)
        {
            if (points == null || points.Count == 0)
                return false;

            var leftPercent = AppConfig.TouchScreenBlockLeftPercent;
            var topPercent = AppConfig.TouchScreenBlockTopPercent;
            var rightPercent = AppConfig.TouchScreenBlockRightPercent;
            var bottomPercent = AppConfig.TouchScreenBlockBottomPercent;
            if (leftPercent == 0 && topPercent == 0 && rightPercent == 0 && bottomPercent == 0)
                return false;

            foreach (var inputPoint in points)
            {
                var point = inputPoint.Point;
                var bounds = System.Windows.Forms.Screen.FromPoint(point).Bounds;
                var leftBoundary = bounds.Left + bounds.Width * leftPercent / 100d;
                var topBoundary = bounds.Top + bounds.Height * topPercent / 100d;
                var rightBoundary = bounds.Right - bounds.Width * rightPercent / 100d;
                var bottomBoundary = bounds.Bottom - bounds.Height * bottomPercent / 100d;
                if (point.X < leftBoundary || point.Y < topBoundary || point.X >= rightBoundary || point.Y >= bottomBoundary)
                    return true;
            }

            return false;
        }

        private static bool IsInSystemCaptionButtonArea(IReadOnlyCollection<InputPoint> points, out Point matchedPoint)
        {
            matchedPoint = Point.Empty;
            if (points == null || points.Count == 0)
                return false;

            foreach (var inputPoint in points)
            {
                var point = inputPoint.Point;
                if (IsSystemCaptionButton(point) || IsScreenCornerCaptionFallback(point))
                {
                    matchedPoint = point;
                    return true;
                }
            }

            return false;
        }

        private bool IsInTouchScreenPassthroughWindow(IReadOnlyCollection<InputPoint> points, out Point matchedPoint)
        {
            matchedPoint = Point.Empty;
            if (points == null || points.Count == 0)
                return false;

            foreach (var inputPoint in points)
            {
                var point = inputPoint.Point;
                var hwnd = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
                if (hwnd == IntPtr.Zero)
                    continue;

                var root = GetAncestor(hwnd, GA_ROOT);
                if (root != IntPtr.Zero)
                    hwnd = root;

                lock (_touchScreenPassthroughWindowLock)
                {
                    if (!_touchScreenPassthroughWindows.Contains(hwnd))
                        continue;
                }

                matchedPoint = point;
                return true;
            }

            return false;
        }

        private static bool IsSystemCaptionButton(Point point)
        {
            try
            {
                var hwnd = WindowFromPoint(new NativePoint { X = point.X, Y = point.Y });
                if (hwnd == IntPtr.Zero)
                    return false;

                var root = GetAncestor(hwnd, GA_ROOT);
                if (root != IntPtr.Zero)
                    hwnd = root;

                var packedPoint = new IntPtr(unchecked((int)(((uint)point.Y & 0xffff) << 16 | ((uint)point.X & 0xffff))));
                if (SendMessageTimeout(
                        hwnd,
                        WM_NCHITTEST,
                        IntPtr.Zero,
                        packedPoint,
                        SMTO_BLOCK | SMTO_ABORTIFHUNG,
                        50,
                        out var hitTestResult) != IntPtr.Zero)
                {
                    var hitTest = unchecked((int)hitTestResult.ToUInt64());
                    if (hitTest == HTMINBUTTON || hitTest == HTMAXBUTTON || hitTest == HTCLOSE)
                        return true;
                }

                // Some custom title bars report HTCLIENT for their system-drawn
                // buttons. Fall back to the DPI-scaled right-hand caption strip,
                // but only for a real captioned top-level window.
                var style = GetWindowLong(hwnd, GWL_STYLE);
                if ((style & WS_CAPTION) == 0 || (style & WS_SYSMENU) == 0 || !GetWindowRect(hwnd, out var rect))
                    return false;

                var dpi = GetDpiForWindow(hwnd);
                if (dpi == 0)
                    dpi = 96;
                var buttonWidth = Math.Max(GetSystemMetricsForDpi(SM_CXSIZE, dpi), (int)Math.Ceiling(46d * dpi / 96d));
                var buttonHeight = Math.Max(GetSystemMetricsForDpi(SM_CYSIZE, dpi), (int)Math.Ceiling(32d * dpi / 96d));
                return point.X >= rect.Right - buttonWidth * 3 &&
                       point.X < rect.Right &&
                       point.Y >= rect.Top &&
                       point.Y < rect.Top + buttonHeight;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsScreenCornerCaptionFallback(Point point)
        {
            try
            {
                const int fallbackWidth = 180;
                const int fallbackHeight = 72;
                var bounds = System.Windows.Forms.Screen.FromPoint(point).Bounds;
                return point.Y - bounds.Top <= fallbackHeight &&
                       point.X - bounds.Left >= bounds.Width - fallbackWidth;
            }
            catch
            {
                return false;
            }
        }

        private string ResolveActionGestureName(string recognizedGestureName, IReadOnlyCollection<List<Point>> points)
        {
            var actionGestureName = PreviewActionGestureName(points);
            if (!string.IsNullOrWhiteSpace(actionGestureName) &&
                ApplicationManager.Instance.GetRecognizedDefinedAction(actionGestureName)?.Any() == true)
            {
                if (!string.Equals(actionGestureName, recognizedGestureName, StringComparison.OrdinalIgnoreCase))
                {
                    Logging.LogMessage($"Gesture action-aware match applied. Original={recognizedGestureName ?? "(null)"}, Preferred={actionGestureName}");
                }

                return actionGestureName;
            }

            if (string.IsNullOrWhiteSpace(_fallbackGestureName))
                return recognizedGestureName;

            if (!string.IsNullOrWhiteSpace(recognizedGestureName) &&
                ApplicationManager.Instance.GetRecognizedDefinedAction(recognizedGestureName)?.Any() == true)
            {
                return recognizedGestureName;
            }

            var totalPointCount = CountGesturePoints(points);
            var maxTrailingPointCount = Math.Max(12, totalPointCount / 3);
            if (totalPointCount - _fallbackGesturePointCount > maxTrailingPointCount)
                return recognizedGestureName;

            if (ApplicationManager.Instance.GetRecognizedDefinedAction(_fallbackGestureName)?.Any() != true)
                return recognizedGestureName;

            Logging.LogMessage($"Gesture fallback applied. Original={recognizedGestureName ?? "(null)"}, Fallback={_fallbackGestureName}, Action={_fallbackGestureActionName}, TrailingPoints={totalPointCount - _fallbackGesturePointCount}");
            return _fallbackGestureName;
        }

        //private void CancelCapture(int num)
        //{
        //    // Notify subscribers that gesture capture has been canceled
        //    OnCaptureCanceled(new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values)));
        //}

        private void AddPoint(List<InputPoint> point)
        {
            if (Mode == CaptureMode.Training && SourceDevice == Devices.TouchPad && _pointsCaptured?.Count > 1 && point.Count > 1)
            {
                AddTrainingPointByNearestStroke(point);
                return;
            }

            bool getNewPoint = false;
            int threshold = AppConfig.MinimumPointDistance;
            foreach (var p in point)
            {
                // Don't accept point if it's within specified distance of last point unless it's the first point
                if (_pointsCaptured.TryGetValue(p.ContactIdentifier, out List<Point> stroke))
                {
                    if (stroke.Count != 0)
                    {
                        if (PointPatternMath.GetDistance(stroke.Last(), p.Point) < threshold)
                            continue;

                        if (State == CaptureState.CapturingInvalid)
                            State = CaptureState.Capturing;
                    }

                    getNewPoint = true;
                    // Add point to captured points list
                    stroke.Add(p.Point);
                }
            }
            if (getNewPoint)
            {
                // Notify subscribers that point has been captured
                OnPointCaptured(SourceDevice == Devices.TouchPad
                    ? CreateTouchPadVisualPoints(point)
                    : new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), point.Select(p => p.Point).ToList()));
            }
        }

        private PointsCapturedEventArgs CreateTouchPadVisualPoints(List<InputPoint> rawPoints)
        {
            if (_touchPadRawStartPoints == null || _touchPadVisualPoints == null)
                return new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), rawPoints.Select(p => p.Point).ToList());

            foreach (var raw in rawPoints)
            {
                if (!_touchPadRawStartPoints.TryGetValue(raw.ContactIdentifier, out var rawStart))
                    _touchPadRawStartPoints[raw.ContactIdentifier] = rawStart = raw.Point;

                if (!_touchPadVisualPoints.TryGetValue(raw.ContactIdentifier, out var visualStroke))
                    _touchPadVisualPoints[raw.ContactIdentifier] = visualStroke = new List<Point>(30);

                var visualPoint = ToTouchPadVisualPoint(raw.Point);

                if (visualStroke.Count == 0 || PointPatternMath.GetDistance(visualStroke.Last(), visualPoint) >= 2)
                    visualStroke.Add(visualPoint);
            }

            return new PointsCapturedEventArgs(
                _touchPadVisualPoints.Values.Select(points => new List<Point>(points)).ToList(),
                _touchPadVisualPoints.Values.Select(points => points.FirstOrDefault()).ToList());
        }

        private static PointF GetTouchPadVisualOrigin(List<InputPoint> points)
        {
            if (points == null || points.Count == 0)
                return PointF.Empty;

            return new PointF(
                (float)points.Average(point => point.Point.X),
                (float)points.Average(point => point.Point.Y));
        }

        private Point ToTouchPadVisualPoint(Point rawPoint)
        {
            const float visualScale = 1.0f;
            return new Point(
                _touchPadStartPoint.X + (int)Math.Round((rawPoint.X - _touchPadRawVisualOrigin.X) * visualScale),
                _touchPadStartPoint.Y + (int)Math.Round((rawPoint.Y - _touchPadRawVisualOrigin.Y) * visualScale));
        }

        private void AddTrainingPointByNearestStroke(List<InputPoint> points)
        {
            bool getNewPoint = false;
            int threshold = AppConfig.MinimumPointDistance;
            var assignments = points
                .Select(input => new
                {
                    Input = input,
                    VisualPoint = ToTouchPadVisualPoint(input.Point)
                })
                .SelectMany(input => _pointsCaptured
                    .Where(stroke => stroke.Value.Count > 0)
                    .Select(stroke => new
                    {
                        input.Input,
                        input.VisualPoint,
                        Stroke = stroke,
                        Distance = PointPatternMath.GetDistance(stroke.Value.Last(), input.VisualPoint)
                    }))
                .OrderBy(item => item.Distance)
                .ToList();
            var usedInputs = new HashSet<int>();
            var usedStrokes = new HashSet<int>();

            foreach (var item in assignments)
            {
                if (usedInputs.Contains(item.Input.ContactIdentifier) || usedStrokes.Contains(item.Stroke.Key))
                    continue;

                if (item.Distance < threshold)
                {
                    usedInputs.Add(item.Input.ContactIdentifier);
                    usedStrokes.Add(item.Stroke.Key);
                    continue;
                }

                item.Stroke.Value.Add(item.VisualPoint);
                usedInputs.Add(item.Input.ContactIdentifier);
                usedStrokes.Add(item.Stroke.Key);
                getNewPoint = true;
                if (State == CaptureState.CapturingInvalid)
                    State = CaptureState.Capturing;
            }

            foreach (var input in points.Where(input => !usedInputs.Contains(input.ContactIdentifier)))
            {
                if (_pointsCaptured.TryGetValue(input.ContactIdentifier, out var stroke))
                {
                    var visualPoint = ToTouchPadVisualPoint(input.Point);
                    if (stroke.Count == 0 || PointPatternMath.GetDistance(stroke.Last(), visualPoint) >= threshold)
                    {
                        stroke.Add(visualPoint);
                        getNewPoint = true;
                    }
                }
            }

            if (getNewPoint)
                OnPointCaptured(new PointsCapturedEventArgs(new List<List<Point>>(_pointsCaptured.Values), _pointsCaptured.Values.Select(p => p.FirstOrDefault()).ToList()));
        }



        #endregion

        #region Public Methods

        public void Load()
        {
            // Shortcut method to control singleton instantiation
        }

        public void ToggleUserDisablePointCapture()
        {
            // Toggle User selected Gesture Disabling
            // Added UserDisabled to CaptureState enum since Ready and Disabled can't be used
            // due to the existing logic of Enabling/Disabling for UI/menu popup/etc.
            // The reason I had to set state to Ready if !UserDisabled was due to the sequence of the tray events.
            // I originally had to set to Disable since if you're in the popup it's disabled, however, the popup onclose
            // fires before the menu item's code, so it was back to Ready before this block was executed.  Although, it probably 
            // makes more sense to set it to Ready in the event this is called from another location.
            Mode = Mode == CaptureMode.UserDisabled ? CaptureMode.Normal : CaptureMode.UserDisabled;
        }

        #endregion
    }
}
