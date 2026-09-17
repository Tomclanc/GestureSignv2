using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using GestureSign.Common.Log;
using ManagedWinapi.Hooks;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Input
{
    internal class InputProvider : IDisposable
    {
        private bool disposedValue = false; // To detect redundant calls
        private MessageWindow _messageWindow;
        private CustomNamedPipeServer _deviceStateServer;
        private int _stateUpdating;
        private MouseActions _hookDrawingButton;
        private volatile bool _suppressPointerMotion;
        private int _suppressedPointerMoveCount;
        internal bool SuppressPointerMotion => _suppressPointerMotion;

        public LowLevelMouseHook LowLevelMouseHook;
        private LowLevelKeyboardHook _keyboardHook;
        public event RawPointsDataMessageEventHandler PointsIntercepted;

        public InputProvider()
        {
            _messageWindow = new MessageWindow();
            _messageWindow.PointsIntercepted += MessageWindow_PointsIntercepted;

            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            LowLevelMouseHook = new LowLevelMouseHook();
            _hookDrawingButton = AppConfig.DrawingButton;
            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.KeyIntercepted += KeyboardHook_KeyIntercepted;
            _keyboardHook.StartHook();
            Logging.LogMessage("Keyboard hook started.");
            if (AppConfig.DrawingButton != MouseActions.None)
                Task.Delay(1000).ContinueWith((t) =>
                {
                    UpdateMouseHookState("InitialDelay");
                }, TaskScheduler.FromCurrentSynchronizationContext());


            SystemEvents.SessionSwitch += new SessionSwitchEventHandler(OnSessionSwitch);
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerModeChanged);

            _deviceStateServer = new CustomNamedPipeServer(Common.Constants.Daemon + "DeviceState", IpcCommands.SynDeviceState,
                () => HidDevice.EnumerateDevices());
        }

        private void KeyboardHook_KeyIntercepted(int msg, int vkCode, int scanCode, int flags, int time, IntPtr dwExtraInfo, ref bool handled)
        {
            if (msg != 0x100 && msg != 0x104)
                return;

            var state = PointCapture.Instance.State;
            if (state != CaptureState.Capturing && state != CaptureState.CapturingInvalid)
                return;

            var actions = ApplicationManager.Instance.RecognizedApplication?
                .Where(app => !(app is IgnoredApp) && app.Actions != null)
                .SelectMany(app => app.Actions)
                .Concat(ApplicationManager.Instance.GetGlobalApplication().Actions)
                .Where(action => action != null
                    && !string.IsNullOrWhiteSpace(action.GestureName)
                    && action.Hotkey != null
                    && action.Hotkey.KeyCode == vkCode);

            if (actions != null && actions.Any())
                handled = true;
        }

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            MouseActions drawingButton = AppConfig.DrawingButton;
            if (drawingButton != _hookDrawingButton)
            {
                _hookDrawingButton = drawingButton;
                UpdateMouseHookState("DrawingButtonChanged");
            }
            UpdateDeviceState();
        }

        internal bool BeginPointerMotionSuppression(Point anchor)
        {
            if (_suppressPointerMotion)
            {
                return LowLevelMouseHook.Hooked;
            }
            Interlocked.Exchange(ref _suppressedPointerMoveCount, 0);
            _suppressPointerMotion = true;
            try
            {
                UpdateMouseHookState("TouchPadEdgeGestureStarted");
                if (!LowLevelMouseHook.Hooked)
                    throw new InvalidOperationException("Mouse hook was not installed.");
            }
            catch (Exception ex)
            {
                _suppressPointerMotion = false;
                Logging.LogMessage("TouchPad edge pointer lock unavailable. Error=" + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
            Logging.LogMessage($"TouchPad edge pointer lock enabled. Anchor={anchor.X},{anchor.Y}");
            return LowLevelMouseHook.Hooked;
        }

        internal void RecordSuppressedPointerMove()
        {
            Interlocked.Increment(ref _suppressedPointerMoveCount);
        }

        internal void EndPointerMotionSuppression(string reason)
        {
            if (_suppressPointerMotion)
            {
                _suppressPointerMotion = false;
                int value = Interlocked.Exchange(ref _suppressedPointerMoveCount, 0);
                try
                {
                    UpdateMouseHookState("TouchPadEdgeGestureEnded");
                }
                catch (Exception ex)
                {
                    Logging.LogMessage($"TouchPad edge pointer lock cleanup failed. Reason={reason}, Error={ex.GetType().Name}: {ex.Message}");
                }
                Logging.LogMessage($"TouchPad edge pointer lock disabled. Reason={reason}, SuppressedMoves={value}");
            }
        }

        private void UpdateMouseHookState(string reason)
        {
            if (disposedValue)
                return;
            bool flag = _hookDrawingButton != MouseActions.None || _suppressPointerMotion;
            if (flag && !LowLevelMouseHook.Hooked)
            {
                LowLevelMouseHook.StartHook();
                Logging.LogMessage($"Mouse hook started. Reason={reason}, DrawingButton={_hookDrawingButton}, PointerLock={_suppressPointerMotion}");
            }
            else if (!flag && LowLevelMouseHook.Hooked)
            {
                LowLevelMouseHook.Unhook();
                Logging.LogMessage("Mouse hook stopped. Reason=" + reason + ", DrawingButton=None, PointerLock=False");
            }
        }

        private void MessageWindow_PointsIntercepted(object sender, RawPointsDataMessageEventArgs e)
        {
            if (e.RawData.Count == 0)
                return;
            PointsIntercepted?.Invoke(this, e);
        }

        internal void ResetSourceDevice(Devices sourceDevice)
        {
            _messageWindow.RequestSourceDeviceReset(sourceDevice);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
            {
                UpdateDeviceState();
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            // We need to handle sleeping(and other related events)
            // This is so we never lose the lock on the touchpad hardware.
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.SessionUnlock:
                    UpdateDeviceState();
                    break;
                default:
                    break;
            }
        }

        private void UpdateDeviceState()
        {
            if (0 == System.Threading.Interlocked.Exchange(ref _stateUpdating, 1))
            {
                Task.Delay(600).ContinueWith((t) =>
                {
                    System.Threading.Interlocked.Exchange(ref _stateUpdating, 0);
                    _messageWindow.UpdateRegistration();
                }, TaskScheduler.FromCurrentSynchronizationContext());
            }
        }

        #region IDisposable Support

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    AppConfig.ConfigChanged -= AppConfig_ConfigChanged;
                }

                SystemEvents.SessionSwitch -= OnSessionSwitch;
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                if (_keyboardHook != null)
                {
                    _keyboardHook.KeyIntercepted -= KeyboardHook_KeyIntercepted;
                    _keyboardHook.Unhook();
                }
                _suppressPointerMotion = false;
                LowLevelMouseHook?.Unhook();
                _deviceStateServer.Dispose();
                disposedValue = true;
            }
        }

        ~InputProvider()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }
}
