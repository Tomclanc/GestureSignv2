using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using ManagedWinapi.Windows;
using System.Runtime.InteropServices;

namespace GestureSign.Daemon.Input
{
    public partial class PointEventTranslator
    {
        private const int CaptionButtonWidth = 180;
        private const int CaptionButtonHeight = 72;
        private readonly InputProvider _inputProvider;
        private int _lastPointsCount;
        private DateTime _lastTouchInputUtc;
        private HashSet<MouseActions> _pressedMouseButton;
        private System.Threading.Timer _touchPadReleaseTimer;
        private List<RawData> _lastTouchPadRawData;
        private readonly TouchScreenReleaseTracker _touchScreenRelease = new TouchScreenReleaseTracker();
        private readonly Dictionary<int, RawData> _activeTouchScreenContacts = new Dictionary<int, RawData>();
        // Keep the order in which touchscreen contacts first appeared. The
        // active-contact dictionary is keyed by HID id, which is not a finger
        // order and must not be used for mouse-action target selection.
        private readonly List<int> _touchScreenContactOrder = new List<int>();
        private readonly Dictionary<int, RawData> _releasedTouchScreenContacts = new Dictionary<int, RawData>();
        private readonly System.Windows.Forms.Timer _mouseStatePollTimer;
        private readonly System.Windows.Forms.Timer _mouseMoveDispatchTimer;
        private DateTime _lastMouseHookEventUtc;
        private DateTime _mouseCaptureStartedUtc;
        private bool _mousePollingFallbackActive;
        private bool _mousePollingObservedButtonDown;
        private bool _hasPendingMouseMove;
        private System.Drawing.Point _pendingMouseMovePoint;
        private MouseActions _activeMouseDrawingButton = MouseActions.None;

        // A mouse gesture should never be able to monopolize normal clicks for a
        // long period when a driver or a protected app drops the button-up hook.
        private const int MouseCaptureWatchdogMilliseconds = 5000;
        private const int LowLevelMouseInjectedFlag = 0x00000001;

        internal Devices SourceDevice { get; private set; }

        internal PointEventTranslator(InputProvider inputProvider)
        {
            _inputProvider = inputProvider;
            _pressedMouseButton = new HashSet<MouseActions>();
            _touchPadReleaseTimer = new System.Threading.Timer(_ => ReleaseTouchPadIfIdle(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _mouseStatePollTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _mouseStatePollTimer.Tick += (_, __) => PollMouseGestureState();
            _mouseMoveDispatchTimer = new System.Windows.Forms.Timer { Interval = 16 };
            _mouseMoveDispatchTimer.Tick += (_, __) => DispatchPendingMouseMove();
            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            inputProvider.PointsIntercepted += TranslateTouchEvent;
            inputProvider.LowLevelMouseHook.MouseDown += LowLevelMouseHook_MouseDown;
            inputProvider.LowLevelMouseHook.MouseMove += LowLevelMouseHook_MouseMove;
            inputProvider.LowLevelMouseHook.MouseUp += LowLevelMouseHook_MouseUp;
        }

        internal void ResetInputState(string reason)
        {
            Logging.LogMessage($"Input translation reset. Reason={reason}, Source={SourceDevice}, ActiveTouchContacts={_activeTouchScreenContacts.Count}, ReleasedTouchContacts={_releasedTouchScreenContacts.Count}");
            ResetTipTap();
            _touchPadReleaseTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _lastTouchPadRawData = null;
            _lastPointsCount = 0;
            _activeTouchScreenContacts.Clear();
            _releasedTouchScreenContacts.Clear();
            _touchScreenContactOrder.Clear();
            _touchScreenRelease.Reset();
            _pressedMouseButton.Clear();
            ResetMouseGestureTracking();
            SourceDevice = Devices.None;
        }
        internal bool TestPenPreemption()
        {
            // Exercise the real raw-event adapter without completing a gesture
            // or dispatching an action. The startup test runs in isolated data.
            var point = new System.Drawing.Point(400, 400);
            TranslateTouchEvent(this, new RawPointsDataMessageEventArgs(
                new List<RawData> { new RawData(DeviceStates.Tip, 901, point) }, Devices.TouchScreen));
            TranslateTouchEvent(this, new RawPointsDataMessageEventArgs(
                new List<RawData> { new RawData(DeviceStates.Tip | DeviceStates.InRange | DeviceStates.Eraser, 0, point) }, Devices.Pen));
            return _activeTouchScreenContacts.Count == 0 && _releasedTouchScreenContacts.Count == 0 &&
                !_touchScreenRelease.WaitingForContact;
        }

        internal bool TestMissingTouchRelease()
        {
            ResetInputState("MissingTouchReleaseSelfTest");
            var point = new System.Drawing.Point(400, 400);
            TranslateTouchEvent(this, new RawPointsDataMessageEventArgs(
                new List<RawData> { new RawData(DeviceStates.Tip, 901, point), new RawData(DeviceStates.Tip, 902, point) }, Devices.TouchScreen, true));
            TranslateTouchEvent(this, new RawPointsDataMessageEventArgs(
                new List<RawData> { new RawData(DeviceStates.None, 903, point) }, Devices.TouchScreen, true));
            return InputStateCleared && PointCapture.Instance.Mode == CaptureMode.UserDisabled;
        }

        internal bool InputStateCleared => SourceDevice == Devices.None && _lastPointsCount == 0 &&
            _activeTouchScreenContacts.Count == 0 && _releasedTouchScreenContacts.Count == 0 &&
            !_touchScreenRelease.WaitingForContact && _pressedMouseButton.Count == 0;
        internal void Dispose()
        {
            ResetTipTap();
            AppConfig.ConfigChanged -= AppConfig_ConfigChanged;
            _mouseMoveDispatchTimer?.Dispose();
            _mouseStatePollTimer?.Dispose();
            _touchPadReleaseTimer?.Dispose();
        }

        #region Custom Events

        public event EventHandler<InputPointsEventArgs> PointDown;

        protected virtual void OnPointDown(InputPointsEventArgs args)
        {
            if (SourceDevice != Devices.None && SourceDevice != args.PointSource && args.PointSource != Devices.Pen) return;
            SourceDevice = args.PointSource;
            PointDown?.Invoke(this, args);
        }

        public event EventHandler<InputPointsEventArgs> PointUp;

        protected virtual void OnPointUp(InputPointsEventArgs args)
        {
            if (SourceDevice != Devices.None && SourceDevice != args.PointSource) return;

            PointUp?.Invoke(this, args);

            SourceDevice = Devices.None;
        }

        public event EventHandler<InputPointsEventArgs> PointMove;

        protected virtual void OnPointMove(InputPointsEventArgs args)
        {
            if (SourceDevice != args.PointSource) return;
            PointMove?.Invoke(this, args);
        }

        #endregion

        #region Private Methods

        private void LowLevelMouseHook_MouseUp(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            _lastMouseHookEventUtc = DateTime.UtcNow;
            if (_inputProvider.FilterEdgeClick(mouseMessage, false)) { handled = true; return; }
            if (IsInjectedMouseMessage(mouseMessage))
                return;

            var button = (MouseActions)mouseMessage.Button;
            if (ShouldPassThroughGestureSignUi(mouseMessage.Point) && !_pressedMouseButton.Contains(button))
                return;

            if (ShouldPassThroughRemoteDesktopInput(mouseMessage.Point) && !_pressedMouseButton.Contains(button))
                return;

            if (ShouldPassThroughGamingShellInput(mouseMessage.Point) && !_pressedMouseButton.Contains(button))
                return;

            if (IsCaptionButtonRegion(mouseMessage.Point) && IsDrawingButton(button) && !_pressedMouseButton.Contains(button))
                return;

            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point) && !IsDrawingButton(button) && !_pressedMouseButton.Contains(button))
                return;

            if (_activeMouseDrawingButton != MouseActions.None && button == _activeMouseDrawingButton)
            {
                DispatchPendingMouseMove();
                var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
                OnPointUp(args);
                handled = args.Handled;
            }
            _pressedMouseButton.Remove(button);
            if (button == _activeMouseDrawingButton)
                ResetMouseGestureTracking();
        }

        private void LowLevelMouseHook_MouseMove(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            _lastMouseHookEventUtc = DateTime.UtcNow;
            if (IsInjectedMouseMessage(mouseMessage))
                return;

            if (_inputProvider.SuppressPointerMotion)
            {
                _inputProvider.RecordSuppressedPointerMove();
                handled = true;
                return;
            }

            var drawingButton = ActiveDrawingButton;
            // WH_MOUSE_LL receives every pointer movement system-wide. Do not do
            // window lookups or gesture work unless the drawing button actually
            // started a capture; those synchronous calls can stall high-report-rate
            // mice and make the desktop appear unresponsive.
            if (!_pressedMouseButton.Contains(drawingButton) || SourceDevice != Devices.Mouse)
                return;

            // A mouse has exactly one pointer. Dispatch immediately so the
            // visual trail and recognizer receive every move in order.
            OnPointMove(new InputPointsEventArgs(
                new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }),
                Devices.Mouse));
            // The button-down was already intercepted. Move messages are left
            // untouched, matching WGestures: applications may still observe
            // cursor motion, but they never receive the original button click.
        }

        private void LowLevelMouseHook_MouseDown(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            _lastMouseHookEventUtc = DateTime.UtcNow;
            if (_inputProvider.FilterEdgeClick(mouseMessage, true)) { handled = true; return; }
            if (IsInjectedMouseMessage(mouseMessage))
                return;

            if (IsDrawingButton((MouseActions)mouseMessage.Button) &&
                (SourceDevice == Devices.TouchScreen || SourceDevice == Devices.Pen || _touchScreenRelease.WaitingForContact) &&
                (DateTime.UtcNow - _lastTouchInputUtc).TotalSeconds > 2)
            {
                PointCapture.Instance.CancelInputCapture("StaleTouchBeforeMouse");
                _inputProvider.ResetRawInputState();
            }
            RecoverStaleMouseCaptureBeforeNewInput((MouseActions)mouseMessage.Button);

            if (ShouldPassThroughGestureSignUi(mouseMessage.Point))
            {
                if (IsDrawingButton((MouseActions)mouseMessage.Button))
                    Logging.LogMessage($"Mouse gesture passed through. Reason=GestureSignUi, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (ShouldPassThroughRemoteDesktopInput(mouseMessage.Point))
            {
                if (IsDrawingButton((MouseActions)mouseMessage.Button))
                    Logging.LogMessage($"Mouse gesture passed through. Reason=RemoteDesktop, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (ShouldPassThroughGamingShellInput(mouseMessage.Point))
            {
                if (IsDrawingButton((MouseActions)mouseMessage.Button))
                    Logging.LogMessage($"Mouse gesture passed through. Reason=GamingShell, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (IsCaptionButtonRegion(mouseMessage.Point) && IsDrawingButton((MouseActions)mouseMessage.Button))
            {
                Logging.LogMessage($"Mouse gesture ignored. Reason=CaptionButtonRegion, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point))
                return;

            if (IsDrawingButton((MouseActions)mouseMessage.Button) && _pressedMouseButton.Count == 0)
            {
                Logging.LogMessage($"Mouse gesture button down. Button={(MouseActions)mouseMessage.Button}, DrawingButton={AppConfig.DrawingButton}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                var drawingButton = (MouseActions)mouseMessage.Button;
                var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
                _activeMouseDrawingButton = drawingButton;
OnPointDown(args);
                // Mouse gestures own the complete button cycle immediately.
                // Keep the capture provisional until movement is observed;
                // PointCapture will re-inject a normal click on button-up when
                // no effective movement occurred (the WGestures/GestureSign
                // reference behavior).
                PointCapture.Instance.State = CaptureState.CapturingInvalid;
                handled = args.Handled;
                _activeMouseDrawingButton = drawingButton;
                _mouseCaptureStartedUtc = DateTime.UtcNow;
                _hasPendingMouseMove = false;
                Logging.LogMessage($"Mouse gesture capture accepted. Button={drawingButton}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
            }
            _pressedMouseButton.Add((MouseActions)mouseMessage.Button);
            if (IsDrawingButton((MouseActions)mouseMessage.Button))
            {
                _mousePollingFallbackActive = false;
                _mousePollingObservedButtonDown = false;
            }
        }

        private void PollMouseGestureState()
        {
            var drawingButton = ActiveDrawingButton;
            if (SourceDevice != Devices.Mouse || !_pressedMouseButton.Contains(drawingButton))
            {
                ResetMouseGestureTracking();
                return;
            }

            if (_mouseCaptureStartedUtc != default(DateTime) &&
                (DateTime.UtcNow - _mouseCaptureStartedUtc).TotalMilliseconds >= MouseCaptureWatchdogMilliseconds)
            {
                Logging.LogMessage($"Mouse gesture capture reset by watchdog. Button={drawingButton}, DurationMs={MouseCaptureWatchdogMilliseconds}");
                CancelActiveMouseGesture("WatchdogTimeout");
                return;
            }

            var point = System.Windows.Forms.Cursor.Position;
            if (IsMouseButtonDown(drawingButton))
            {
                _mousePollingObservedButtonDown = true;
                // Normal low-level hook events remain authoritative. Polling only
                // fills a gap after the hook has gone quiet over a protected window.
                if ((DateTime.UtcNow - _lastMouseHookEventUtc).TotalMilliseconds < 35)
                    return;

                if (!_mousePollingFallbackActive)
                {
                    _mousePollingFallbackActive = true;
                    Logging.LogMessage($"Mouse gesture polling fallback started. Button={drawingButton}, Point={point.X},{point.Y}");
                }

                QueueMouseMove(point);
                return;
            }

            if (!_mousePollingObservedButtonDown)
            {
                // RDP and some synthetic input sources do not update asynchronous
                // key state. Never invent a release unless polling first observed
                // the button as genuinely pressed; the hook remains authoritative.
                if ((DateTime.UtcNow - _lastMouseHookEventUtc).TotalMilliseconds >= 120)
                    _mouseStatePollTimer.Stop();
                return;
            }

            _pressedMouseButton.Remove(drawingButton);
            DispatchPendingMouseMove();
            var args = new InputPointsEventArgs(
                new List<InputPoint>(new[] { new InputPoint(1, point) }),
                Devices.Mouse);
            OnPointUp(args);
            Logging.LogMessage($"Mouse gesture polling fallback released. Button={drawingButton}, Active={_mousePollingFallbackActive}, Point={point.X},{point.Y}");
            ResetMouseGestureTracking();
        }

        private static bool IsDrawingButton(MouseActions button) => button != MouseActions.None && (AppConfig.DrawingButton & button) == button;
        public MouseActions ActiveDrawingButton =>
            _activeMouseDrawingButton != MouseActions.None ? _activeMouseDrawingButton : AppConfig.DrawingButton;

        internal void CancelActiveMouseGesture(string reason)
        {
            if (SourceDevice == Devices.Mouse)
                PointCapture.Instance.CancelMouseCapture(reason);

            SourceDevice = Devices.None;
            _pressedMouseButton.Clear();
            ResetMouseGestureTracking();
        }

        private void RecoverStaleMouseCaptureBeforeNewInput(MouseActions incomingButton)
        {
            if (SourceDevice != Devices.Mouse || _activeMouseDrawingButton == MouseActions.None)
                return;

            var elapsed = (DateTime.UtcNow - _mouseCaptureStartedUtc).TotalMilliseconds;
            var activeButtonStillDown = IsMouseButtonDown(_activeMouseDrawingButton);
            if (activeButtonStillDown &&
                (_activeMouseDrawingButton != incomingButton || elapsed < MouseCaptureWatchdogMilliseconds))
                return;

            Logging.LogMessage($"Stale mouse gesture recovered before new input. Active={_activeMouseDrawingButton}, Incoming={incomingButton}, DurationMs={(int)elapsed}, ButtonStillDown={activeButtonStillDown}");
            CancelActiveMouseGesture("StaleBeforeNewInput");
        }

        private static bool IsInjectedMouseMessage(LowLevelMouseMessage mouseMessage)
        {
            return (mouseMessage.Flags & LowLevelMouseInjectedFlag) != 0;
        }

        private void QueueMouseMove(System.Drawing.Point point)
        {
            _pendingMouseMovePoint = point;
            _hasPendingMouseMove = true;
        }

        private void DispatchPendingMouseMove()
        {
            if (!_hasPendingMouseMove || SourceDevice != Devices.Mouse)
                return;

            _hasPendingMouseMove = false;
            OnPointMove(new InputPointsEventArgs(
                new List<InputPoint>(new[] { new InputPoint(1, _pendingMouseMovePoint) }),
                Devices.Mouse));
        }

        private void ResetMouseGestureTracking()
        {
            _mouseStatePollTimer.Stop();
            _mouseMoveDispatchTimer.Stop();
            _mousePollingFallbackActive = false;
            _mousePollingObservedButtonDown = false;
            _hasPendingMouseMove = false;
            _activeMouseDrawingButton = MouseActions.None;
            _mouseCaptureStartedUtc = default(DateTime);
        }

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            ResetTipTap();
            if (_activeMouseDrawingButton == MouseActions.None ||
                IsDrawingButton(_activeMouseDrawingButton))
                return;

            Logging.LogMessage($"Mouse gesture capture reset after drawing button changed. Previous={_activeMouseDrawingButton}, Current={AppConfig.DrawingButton}");
            PointCapture.Instance.CancelMouseCapture("DrawingButtonChanged");
            SourceDevice = Devices.None;
            _pressedMouseButton.Clear();
            ResetMouseGestureTracking();
        }

        private static bool IsRemoteSession()
        {
            const int smRemoteSession = 0x1000;
            return GetSystemMetrics(smRemoteSession) != 0;
        }

        private static bool IsMouseButtonDown(MouseActions button)
        {
            int virtualKey;
            switch (button)
            {
                case MouseActions.Left:
                    virtualKey = 0x01;
                    break;
                case MouseActions.Right:
                    virtualKey = 0x02;
                    break;
                case MouseActions.Middle:
                    virtualKey = 0x04;
                    break;
                case MouseActions.XButton1:
                    virtualKey = 0x05;
                    break;
                case MouseActions.XButton2:
                    virtualKey = 0x06;
                    break;
                default:
                    return false;
            }

            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int virtualKey);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);

        private static bool ShouldPreferMouseGesturesAtPoint(System.Drawing.Point point)
        {
            if (!AppConfig.PreferEdgeMouseGestures)
                return false;

            try
            {
                var targetWindow = SystemWindow.FromPointEx(point.X, point.Y, true, true);
                ApplicationManager.GetWindowInfo(targetWindow, out _, out _, out var fileName);
                return string.Equals(fileName, "msedge.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldPassThroughRemoteDesktopInput(System.Drawing.Point point)
        {
            try
            {
                var targetWindow = SystemWindow.FromPointEx(point.X, point.Y, true, true);
                ApplicationManager.GetWindowInfo(targetWindow, out _, out _, out var fileName);
                return IsRemoteDesktopProcess(fileName);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldPassThroughGamingShellInput(System.Drawing.Point point)
        {
            try
            {
                var targetWindow = SystemWindow.FromPointEx(point.X, point.Y, true, true);
                if (IsGamingShellWindow(targetWindow))
                    return true;

                // Packaged Xbox/Game Bar surfaces can be hit-tested as a XAML host.
                // The foreground top-level window is the reliable process owner.
                return IsGamingShellWindow(SystemWindow.ForegroundWindow);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGamingShellWindow(SystemWindow window)
        {
            if (window == null || window.HWnd == IntPtr.Zero)
                return false;

            try
            {
                ApplicationManager.GetWindowInfo(window, out _, out _, out var fileName);
                return string.Equals(fileName, "XboxPcApp.exe", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "XboxPcAppFT.exe", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "GamingApp.exe", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "GameBar.exe", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(fileName, "GameBarFTServer.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool ShouldPassThroughGestureSignUi(System.Drawing.Point point)
        {
            try
            {
                var targetWindow = SystemWindow.FromPointEx(point.X, point.Y, true, true);
                if (IsGestureSignUiWindow(targetWindow))
                    return true;

                // Packaged WinUI content and its transient text-selection/context-menu
                // surfaces can be reported through a Windows host window instead of
                // GestureSign.WinUI.exe. The foreground top-level window remains the
                // reliable owner in that case.
                return IsGestureSignUiWindow(SystemWindow.ForegroundWindow);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsGestureSignUiWindow(SystemWindow window)
        {
            if (window == null || window.HWnd == IntPtr.Zero)
                return false;

            try
            {
                ApplicationManager.GetWindowInfo(window, out _, out var title, out var fileName);
                if (string.Equals(fileName, "GestureSign.WinUI.exe", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fileName, "GestureSign.WinUI", StringComparison.OrdinalIgnoreCase))
                    return true;

                // ApplicationFrameHost and XAML popup windows may own the hit-test
                // handle for packaged UI. Limit the title fallback to the exact app
                // window so other applications keep their configured mouse gestures.
                return string.Equals(title?.Trim(), "GestureSign V2", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsRemoteDesktopProcess(string fileName)
        {
            return string.Equals(fileName, "mstsc.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "msrdc.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "RdClient.Windows.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "Windows365.exe", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(fileName, "vmconnect.exe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsCaptionButtonRegion(System.Drawing.Point point)
        {
            try
            {
                var screen = System.Windows.Forms.Screen.FromPoint(point);
                var bounds = screen.Bounds;
                var x = point.X - bounds.Left;
                var y = point.Y - bounds.Top;
                return y <= CaptionButtonHeight &&
                       (x <= CaptionButtonWidth || x >= bounds.Width - CaptionButtonWidth);
            }
            catch
            {
                return false;
            }
        }

        private void TranslateTouchEvent(object sender, RawPointsDataMessageEventArgs e)
        {
            _lastTouchInputUtc = DateTime.UtcNow;
            if (e.SourceDevice == Devices.TouchPad && TranslateTipTap(e)) return;
            if (e.SourceDevice != Devices.TouchPad) ResetTipTap();
            if (e.SourceDevice == Devices.TouchScreen)
            {
                TranslateTouchScreenEvent(e);
                return;
            }

            if ((e.SourceDevice & Devices.TouchDevice) != 0)
            {
                var rawData = e.RawData;

                int releaseCount = rawData.Count(rtd => rtd.State == 0);

                if (SourceDevice == Devices.None && rawData.Count > 0 && releaseCount == 0)
                {
                    _lastPointsCount = rawData.Count;
                    OnPointDown(new InputPointsEventArgs(rawData, e.SourceDevice));

                    ArmTouchPadRelease(e.SourceDevice, rawData);

                    return;
                }

                if (rawData.Count == _lastPointsCount)
                {
                    if (releaseCount != 0)
                    {
                        OnPointUp(new InputPointsEventArgs(rawData, e.SourceDevice));
                        _lastPointsCount -= releaseCount;
                        ResetTouchStateIfReleased(rawData);
                        return;
                    }
                    OnPointMove(new InputPointsEventArgs(rawData, e.SourceDevice));
                }
                else if (rawData.Count > _lastPointsCount)
                {
                    if (releaseCount != 0)
                    {
                        if (releaseCount == rawData.Count)
                        {
                            OnPointUp(new InputPointsEventArgs(rawData, e.SourceDevice));
                            ResetTouchStateIfReleased(rawData);
                        }
                        return;
                    }
                    if (PointCapture.Instance.InputPoints.Any(p => p.Count > 10))
                    {
                        OnPointMove(new InputPointsEventArgs(rawData, e.SourceDevice));
                        return;
                    }
                    _lastPointsCount = rawData.Count;
                    OnPointDown(new InputPointsEventArgs(rawData, e.SourceDevice));
                }
                else
                {
                    OnPointUp(new InputPointsEventArgs(rawData, e.SourceDevice));
                    _lastPointsCount = _lastPointsCount - rawData.Count > releaseCount ? rawData.Count : _lastPointsCount - releaseCount;
                    ResetTouchStateIfReleased(rawData);
                }

                if (rawData.Count > 0 && releaseCount == 0)
                    ArmTouchPadRelease(e.SourceDevice, rawData);
            }
            else if (e.SourceDevice == Devices.Pen)
            {
                if (_activeTouchScreenContacts.Count > 0 || _releasedTouchScreenContacts.Count > 0)
                    PointCapture.Instance.CancelInputCapture("PenPreemptedTouch");
                bool release = !PenGesturePolicy.IsActive(AppConfig.PenGestureButton, e.RawData[0].State);
                bool tip = (e.RawData[0].State & (DeviceStates.Eraser | DeviceStates.Tip)) != 0;

                if (release)
                {
                    OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    _lastPointsCount = 0;
                    return;
                }

                var penSetting = PenGesturePolicy.Normalize(AppConfig.PenGestureButton);
                bool drawByTip = (penSetting & DeviceStates.Tip) != 0;
                bool drawByHover = (penSetting & DeviceStates.InRange) != 0;

                if (drawByHover && drawByTip)
                {
                    if (_lastPointsCount == 1 && SourceDevice == Devices.Pen)
                    {
                        OnPointMove(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    }
                    else if (_lastPointsCount >= 0)
                    {
                        _lastPointsCount = 1;
                        OnPointDown(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    }
                }
                else if (drawByTip)
                {
                    if (!tip)
                    {
                        if (SourceDevice == Devices.Pen)
                        {
                            OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                            _lastPointsCount = 0;
                        }
                        return;
                    }

                    if (_lastPointsCount == 1 && SourceDevice == Devices.Pen)
                    {
                        OnPointMove(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    }
                    else if (_lastPointsCount >= 0)
                    {
                        _lastPointsCount = 1;
                        OnPointDown(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    }
                }
                else if (drawByHover)
                {
                    if (_lastPointsCount == 1 && SourceDevice == Devices.Pen)
                    {
                        if (tip)
                        {
                            OnPointDown(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                            _lastPointsCount = -1;
                        }
                        else
                        {
                            OnPointMove(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                        }
                    }
                    else if (_lastPointsCount >= 0)
                    {
                        if (tip)
                        {
                            _lastPointsCount = -1;
                            return;
                        }
                        _lastPointsCount = 1;
                        OnPointDown(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    }
                }
            }
        }

        private void TranslateTouchScreenEvent(RawPointsDataMessageEventArgs e)
        {
            var rawData = e.RawData;
            if (rawData == null || rawData.Count == 0)
                return;

            var missing = TouchScreenFramePolicy.MissingContacts(_activeTouchScreenContacts.Keys,
                rawData.Select(point => point.ContactIdentifier), e.CompleteContactFrame);
            if (missing.Length > 0)
            {
                Logging.LogMessage($"TouchScreen stale contacts canceled. Reason=MissingFromCompleteFrame, MissingIds={string.Join(",", missing)}, ActiveContacts={_activeTouchScreenContacts.Count}, ReleasedContacts={_releasedTouchScreenContacts.Count}, FrameContacts={rawData.Count}");
                PointCapture.Instance.CancelInputCapture("MissingFromCompleteTouchFrame");
            }

            var wasWaitingForContact = _touchScreenRelease.WaitingForContact;
            var newContact = false;
            var previousCount = _activeTouchScreenContacts.Count;
            var releasedContacts = new List<RawData>();
            foreach (var point in rawData)
            {
                newContact |= _touchScreenRelease.Observe(point.ContactIdentifier, point.RawPoints, point.State != DeviceStates.None);
                if (point.State == DeviceStates.None)
                {
                    // Stray or duplicated lift reports are not gesture history.
                    if (!_activeTouchScreenContacts.ContainsKey(point.ContactIdentifier)) continue;
                    // A number of HID drivers omit or reuse coordinates in the
                    // tip-up report. The last active report is the reliable
                    // release location for the current contact.
                    var released = _activeTouchScreenContacts.TryGetValue(point.ContactIdentifier, out var active)
                        ? new RawData(DeviceStates.None, point.ContactIdentifier, active.RawPoints)
                        : point;
                    releasedContacts.Add(released);
                    _releasedTouchScreenContacts[point.ContactIdentifier] = released;
                    _activeTouchScreenContacts.Remove(point.ContactIdentifier);
                }
                else
                {
                    if (!_activeTouchScreenContacts.ContainsKey(point.ContactIdentifier) &&
                        !_touchScreenContactOrder.Contains(point.ContactIdentifier))
                        _touchScreenContactOrder.Add(point.ContactIdentifier);
                    _activeTouchScreenContacts[point.ContactIdentifier] = point;
                }
            }

            var activeContacts = OrderTouchScreenContacts(_activeTouchScreenContacts.Values);

            if (wasWaitingForContact)
            {
                if (_touchScreenRelease.AllReleased)
                {
                    _touchScreenRelease.Reset();
                    _activeTouchScreenContacts.Clear();
                    _releasedTouchScreenContacts.Clear();
                    _touchScreenContactOrder.Clear();
                }
                else if (_touchScreenRelease.TryRearm(newContact))
                {
                    // Start fresh strokes with live anchors and the newly landed finger.
                    _releasedTouchScreenContacts.Clear();
                    _touchScreenContactOrder.Clear();
                    _touchScreenContactOrder.AddRange(activeContacts.Select(point => point.ContactIdentifier));
                    _lastPointsCount = activeContacts.Count;
                    Logging.LogMessage($"TouchScreen anchor gesture rearmed. Contacts={activeContacts.Count}");
                    OnPointDown(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                }
                return;
            }
            if (SourceDevice == Devices.None && activeContacts.Count > 0)
            {
                _lastPointsCount = activeContacts.Count;
                OnPointDown(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                return;
            }

            if (releasedContacts.Count > 0 || activeContacts.Count < previousCount)
            {
                _lastPointsCount = activeContacts.Count;

                if (!_touchScreenRelease.ShouldComplete(releasedContacts.Select(point => point.ContactIdentifier)))
                {
                    OnPointMove(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                    Logging.LogMessage($"TouchScreen release deferred until all contacts are up. ActiveContacts={activeContacts.Count}, ReleasedContacts={_releasedTouchScreenContacts.Count}");
                }
                else
                {
                    Logging.LogMessage($"TouchScreen gesture completed. RemainingAnchors={activeContacts.Count}, ReleasedContacts={_releasedTouchScreenContacts.Count}");
                    OnPointUp(new InputPointsEventArgs(
                        OrderTouchScreenContacts(_releasedTouchScreenContacts.Values.Concat(activeContacts).Select(point => new RawData(DeviceStates.None, point.ContactIdentifier, point.RawPoints))),
                        Devices.TouchScreen));
                    _touchScreenRelease.Complete();
                    if (_touchScreenRelease.WaitingForContact) return;
                    _activeTouchScreenContacts.Clear();
                    _releasedTouchScreenContacts.Clear();
                    _touchScreenContactOrder.Clear();
                }
                return;
            }

            if (_releasedTouchScreenContacts.Count > 0)
            {
                if (activeContacts.Count > 0)
                    OnPointMove(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                return;
            }

            if (activeContacts.Count > previousCount)
            {
                var frameContactCount = rawData.Count(point => point.State != DeviceStates.None);
                if (activeContacts.Count > frameContactCount)
                {
                    Logging.LogMessage($"TouchScreen contact frames merged. FrameContacts={frameContactCount}, ActiveContacts={activeContacts.Count}");
                }

                _lastPointsCount = activeContacts.Count;
                if (PointCapture.Instance.InputPoints.Any(points => points.Count > 10))
                {
                    OnPointMove(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                    return;
                }

                OnPointDown(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
                return;
            }

            if (activeContacts.Count > 0)
                OnPointMove(new InputPointsEventArgs(activeContacts, Devices.TouchScreen));
        }

        private List<RawData> OrderTouchScreenContacts(IEnumerable<RawData> contacts)
        {
            var byIdentifier = contacts
                .GroupBy(point => point.ContactIdentifier)
                .ToDictionary(group => group.Key, group => group.Last());
            var ordered = new List<RawData>(byIdentifier.Count);

            foreach (var identifier in _touchScreenContactOrder)
            {
                if (byIdentifier.TryGetValue(identifier, out var point))
                {
                    ordered.Add(point);
                    byIdentifier.Remove(identifier);
                }
            }

            // Keep deterministic behavior for any contact not observed in the
            // order list, including malformed or late driver frames.
            ordered.AddRange(byIdentifier.Values.OrderBy(point => point.ContactIdentifier));
            return ordered;
        }

        private void ArmTouchPadRelease(Devices sourceDevice, IReadOnlyList<RawData> rawData)
        {
            if (sourceDevice != Devices.TouchPad)
                return;

            _lastTouchPadRawData = rawData
                .Select(point => new RawData(DeviceStates.None, point.ContactIdentifier, point.RawPoints))
                .ToList();
            // Some Precision Touchpad drivers stop reporting instead of sending an
            // explicit all-contacts-up packet. Keep the idle fallback short so an
            // edge gesture executes as soon as the user lifts their finger.
            _touchPadReleaseTimer.Change(120, System.Threading.Timeout.Infinite);
        }

        private void ReleaseTouchPadIfIdle()
        {
            var rawData = _lastTouchPadRawData;
            if (rawData == null || rawData.Count == 0 || SourceDevice != Devices.TouchPad)
                return;

            var tipTapOwned = ResetTipTap();
            if (!tipTapOwned) OnPointUp(new InputPointsEventArgs(rawData, Devices.TouchPad));
            else SourceDevice = Devices.None;
            _inputProvider.ResetSourceDevice(Devices.TouchPad);
            _lastPointsCount = 0;
            _lastTouchPadRawData = null;
        }

        private void ResetTouchStateIfReleased(IReadOnlyList<RawData> rawData)
        {
            if (rawData.Count == 0 || rawData.All(point => point.State == 0))
            {
                _lastPointsCount = 0;
                _lastTouchPadRawData = null;
                _touchPadReleaseTimer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            }
        }

        #endregion
    }
}
