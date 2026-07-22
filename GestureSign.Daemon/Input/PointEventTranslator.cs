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
    public class PointEventTranslator
    {
        private const int CaptionButtonWidth = 180;
        private const int CaptionButtonHeight = 72;
        private int _lastPointsCount;
        private HashSet<MouseActions> _pressedMouseButton;
        private System.Threading.Timer _touchPadReleaseTimer;
        private List<RawData> _lastTouchPadRawData;
        private readonly System.Windows.Forms.Timer _mouseStatePollTimer;
        private DateTime _lastMouseHookEventUtc;
        private bool _mousePollingFallbackActive;
        private bool _mousePollingObservedButtonDown;

        internal Devices SourceDevice { get; private set; }

        internal PointEventTranslator(InputProvider inputProvider)
        {
            _pressedMouseButton = new HashSet<MouseActions>();
            _touchPadReleaseTimer = new System.Threading.Timer(_ => ReleaseTouchPadIfIdle(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            _mouseStatePollTimer = new System.Windows.Forms.Timer { Interval = 12 };
            _mouseStatePollTimer.Tick += (_, __) => PollMouseGestureState();
            inputProvider.PointsIntercepted += TranslateTouchEvent;
            inputProvider.LowLevelMouseHook.MouseDown += LowLevelMouseHook_MouseDown;
            inputProvider.LowLevelMouseHook.MouseMove += LowLevelMouseHook_MouseMove;
            inputProvider.LowLevelMouseHook.MouseUp += LowLevelMouseHook_MouseUp;
        }

        internal void Dispose()
        {
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
            var button = (MouseActions)mouseMessage.Button;
            if (ShouldPassThroughRemoteDesktopInput(mouseMessage.Point) && !_pressedMouseButton.Contains(button))
                return;

            if (IsCaptionButtonRegion(mouseMessage.Point) && button == AppConfig.DrawingButton && !_pressedMouseButton.Contains(button))
                return;

            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point) && button != AppConfig.DrawingButton && !_pressedMouseButton.Contains(button))
                return;

            if (button == AppConfig.DrawingButton)
            {
                var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
                OnPointUp(args);
                handled = args.Handled;
            }
            _pressedMouseButton.Remove(button);
            if (button == AppConfig.DrawingButton)
            {
                _mouseStatePollTimer.Stop();
                _mousePollingFallbackActive = false;
                _mousePollingObservedButtonDown = false;
            }
        }

        private void LowLevelMouseHook_MouseMove(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            _lastMouseHookEventUtc = DateTime.UtcNow;
            if (ShouldPassThroughRemoteDesktopInput(mouseMessage.Point) && !_pressedMouseButton.Contains(AppConfig.DrawingButton))
                return;

            if (IsCaptionButtonRegion(mouseMessage.Point) && !_pressedMouseButton.Contains(AppConfig.DrawingButton))
                return;

            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point) && !_pressedMouseButton.Contains(AppConfig.DrawingButton))
                return;

            var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
            OnPointMove(args);
        }

        private void LowLevelMouseHook_MouseDown(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            _lastMouseHookEventUtc = DateTime.UtcNow;
            if (ShouldPassThroughRemoteDesktopInput(mouseMessage.Point))
            {
                if ((MouseActions)mouseMessage.Button == AppConfig.DrawingButton)
                    Logging.LogMessage($"Mouse gesture passed through. Reason=RemoteDesktop, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (IsCaptionButtonRegion(mouseMessage.Point) && (MouseActions)mouseMessage.Button == AppConfig.DrawingButton)
            {
                Logging.LogMessage($"Mouse gesture ignored. Reason=CaptionButtonRegion, Button={(MouseActions)mouseMessage.Button}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                return;
            }

            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point))
                return;

            if ((MouseActions)mouseMessage.Button == AppConfig.DrawingButton && _pressedMouseButton.Count == 0)
            {
                Logging.LogMessage($"Mouse gesture button down. Button={(MouseActions)mouseMessage.Button}, DrawingButton={AppConfig.DrawingButton}, Point={mouseMessage.Point.X},{mouseMessage.Point.Y}");
                var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
                OnPointDown(args);
                handled = args.Handled;
            }
            _pressedMouseButton.Add((MouseActions)mouseMessage.Button);
            if ((MouseActions)mouseMessage.Button == AppConfig.DrawingButton)
            {
                _mousePollingFallbackActive = false;
                _mousePollingObservedButtonDown = false;
                if (!IsRemoteSession())
                    _mouseStatePollTimer.Start();
            }
        }

        private void PollMouseGestureState()
        {
            var drawingButton = AppConfig.DrawingButton;
            if (SourceDevice != Devices.Mouse || !_pressedMouseButton.Contains(drawingButton))
            {
                _mouseStatePollTimer.Stop();
                _mousePollingFallbackActive = false;
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

                OnPointMove(new InputPointsEventArgs(
                    new List<InputPoint>(new[] { new InputPoint(1, point) }),
                    Devices.Mouse));
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
            _mouseStatePollTimer.Stop();
            var args = new InputPointsEventArgs(
                new List<InputPoint>(new[] { new InputPoint(1, point) }),
                Devices.Mouse);
            OnPointUp(args);
            Logging.LogMessage($"Mouse gesture polling fallback released. Button={drawingButton}, Active={_mousePollingFallbackActive}, Point={point.X},{point.Y}");
            _mousePollingFallbackActive = false;
            _mousePollingObservedButtonDown = false;
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
                bool release = (e.RawData[0].State & (DeviceStates.Invert | DeviceStates.RightClickButton)) == 0 || (e.RawData[0].State & DeviceStates.InRange) == 0;
                bool tip = (e.RawData[0].State & (DeviceStates.Eraser | DeviceStates.Tip)) != 0;

                if (release)
                {
                    OnPointUp(new InputPointsEventArgs(e.RawData, e.SourceDevice));
                    _lastPointsCount = 0;
                    return;
                }

                var penSetting = AppConfig.PenGestureButton;
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

            OnPointUp(new InputPointsEventArgs(rawData, Devices.TouchPad));
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
