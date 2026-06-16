using System;
using System.Collections.Generic;
using System.Linq;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using ManagedWinapi.Windows;

namespace GestureSign.Daemon.Input
{
    public class PointEventTranslator
    {
        private int _lastPointsCount;
        private HashSet<MouseActions> _pressedMouseButton;
        private System.Threading.Timer _trainingTouchPadReleaseTimer;
        private List<RawData> _lastTrainingTouchPadRawData;

        internal Devices SourceDevice { get; private set; }

        internal PointEventTranslator(InputProvider inputProvider)
        {
            _pressedMouseButton = new HashSet<MouseActions>();
            _trainingTouchPadReleaseTimer = new System.Threading.Timer(_ => ReleaseTrainingTouchPad(), null, System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
            inputProvider.PointsIntercepted += TranslateTouchEvent;
            inputProvider.LowLevelMouseHook.MouseDown += LowLevelMouseHook_MouseDown;
            inputProvider.LowLevelMouseHook.MouseMove += LowLevelMouseHook_MouseMove;
            inputProvider.LowLevelMouseHook.MouseUp += LowLevelMouseHook_MouseUp;
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
            var button = (MouseActions)mouseMessage.Button;
            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point) && button != AppConfig.DrawingButton && !_pressedMouseButton.Contains(button))
                return;

            if (button == AppConfig.DrawingButton)
            {
                var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
                OnPointUp(args);
                handled = args.Handled;
            }
            _pressedMouseButton.Remove(button);
        }

        private void LowLevelMouseHook_MouseMove(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
            if (ShouldPreferMouseGesturesAtPoint(mouseMessage.Point) && !_pressedMouseButton.Contains(AppConfig.DrawingButton))
                return;

            var args = new InputPointsEventArgs(new List<InputPoint>(new[] { new InputPoint(1, mouseMessage.Point) }), Devices.Mouse);
            OnPointMove(args);
        }

        private void LowLevelMouseHook_MouseDown(LowLevelMouseMessage mouseMessage, ref bool handled)
        {
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
        }

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

                    if (PointCapture.Instance.Mode == CaptureMode.Training && e.SourceDevice == Devices.TouchPad)
                        ArmTrainingTouchPadRelease(rawData);

                    return;
                }

                if (rawData.Count == _lastPointsCount)
                {
                    if (releaseCount != 0)
                    {
                        OnPointUp(new InputPointsEventArgs(rawData, e.SourceDevice));
                        _lastPointsCount -= releaseCount;
                        return;
                    }
                    OnPointMove(new InputPointsEventArgs(rawData, e.SourceDevice));
                }
                else if (rawData.Count > _lastPointsCount)
                {
                    if (releaseCount != 0)
                        return;
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
                }

                if (PointCapture.Instance.Mode == CaptureMode.Training && e.SourceDevice == Devices.TouchPad && rawData.Count > 0 && releaseCount == 0)
                    ArmTrainingTouchPadRelease(rawData);
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

        private void ArmTrainingTouchPadRelease(IReadOnlyList<RawData> rawData)
        {
            _lastTrainingTouchPadRawData = rawData
                .Select(point => new RawData(DeviceStates.None, point.ContactIdentifier, point.RawPoints))
                .ToList();
            _trainingTouchPadReleaseTimer.Change(450, System.Threading.Timeout.Infinite);
        }

        private void ReleaseTrainingTouchPad()
        {
            var rawData = _lastTrainingTouchPadRawData;
            if (rawData == null || rawData.Count == 0 || SourceDevice != Devices.TouchPad)
                return;

            OnPointUp(new InputPointsEventArgs(rawData, Devices.TouchPad));
            _lastPointsCount = 0;
            _lastTrainingTouchPadRawData = null;
        }

        #endregion
    }
}
