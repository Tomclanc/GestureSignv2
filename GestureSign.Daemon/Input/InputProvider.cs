using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.InterProcessCommunication;
using ManagedWinapi.Hooks;
using Microsoft.Win32;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace GestureSign.Daemon.Input
{
    internal class InputProvider : IDisposable
    {
        private bool disposedValue = false; // To detect redundant calls
        private MessageWindow _messageWindow;
        private CustomNamedPipeServer _deviceStateServer;
        private int _stateUpdating;

        public LowLevelMouseHook LowLevelMouseHook;
        private LowLevelKeyboardHook _keyboardHook;
        public event RawPointsDataMessageEventHandler PointsIntercepted;

        public InputProvider()
        {
            _messageWindow = new MessageWindow();
            _messageWindow.PointsIntercepted += MessageWindow_PointsIntercepted;

            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            LowLevelMouseHook = new LowLevelMouseHook();
            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.KeyIntercepted += KeyboardHook_KeyIntercepted;
            _keyboardHook.StartHook();
            if (AppConfig.DrawingButton != MouseActions.None)
                Task.Delay(1000).ContinueWith((t) =>
                {
                    LowLevelMouseHook.StartHook();
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

        private void AppConfig_ConfigChanged(object sender, System.EventArgs e)
        {
            if (AppConfig.DrawingButton != MouseActions.None)
                LowLevelMouseHook.StartHook();
            else LowLevelMouseHook.Unhook();

            UpdateDeviceState();
        }

        private void MessageWindow_PointsIntercepted(object sender, RawPointsDataMessageEventArgs e)
        {
            if (e.RawData.Count == 0)
                return;
            PointsIntercepted?.Invoke(this, e);
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
