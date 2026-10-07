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
        private readonly SynchronizationContext _ownerContext;
        private MouseActions _hookDrawingButton;
        private volatile bool _suppressPointerMotion;
        private int _suppressedPointerMoveCount;
        internal bool SuppressPointerMotion => _suppressPointerMotion;
        private readonly EdgeClickGate _edgeClickGate = new EdgeClickGate();

        internal void BeginEdgeClickSuppression()
        {
            _edgeClickGate.Begin((System.Windows.Forms.Control.MouseButtons & System.Windows.Forms.MouseButtons.Left) != 0);
            EdgeInputDiagnostics.Record("EDGE_CLICK", "Suppression enabled for configured edge tap");
        }

        internal bool FilterEdgeClick(LowLevelMouseMessage mouse, bool down)
        {
            if (mouse.Button != System.Windows.Forms.MouseButtons.Left || _edgeClickGate == null) return false;
            bool suppressed = _edgeClickGate.Filter(down, (mouse.Flags & 1) != 0);
            if (suppressed)
                EdgeInputDiagnostics.Record("EDGE_CLICK", $"Suppressed native Left{(down ? "Down" : "Up")} PointerLock={_suppressPointerMotion}");
            return suppressed;
        }

        public LowLevelMouseHook LowLevelMouseHook;
        private LowLevelKeyboardHook _keyboardHook;
        public event RawPointsDataMessageEventHandler PointsIntercepted;

        public InputProvider()
        {
            _ownerContext = SynchronizationContext.Current ?? throw new InvalidOperationException("Input requires an owning UI context.");
            _messageWindow = new MessageWindow();
            _messageWindow.PointsIntercepted += MessageWindow_PointsIntercepted;

            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            LowLevelMouseHook = new LowLevelMouseHook();
            LowLevelMouseHook.MessageIntercepted += CleanupEdgeClickHook;
            if (EdgeInputDiagnostics.Enabled)
                LowLevelMouseHook.MessageIntercepted += RecordDiagnosticMouseMessage;
            _hookDrawingButton = AppConfig.DrawingButton;
            _keyboardHook = new LowLevelKeyboardHook();
            _keyboardHook.KeyIntercepted += KeyboardHook_KeyIntercepted;
            _keyboardHook.StartHook();
            Logging.LogMessage("Keyboard hook started.");
            if (AppConfig.DrawingButton != MouseActions.None || EdgeInputDiagnostics.Enabled)
                Task.Delay(1000).ContinueWith((t) =>
                {
                    UpdateMouseHookState("InitialDelay");
                }, TaskScheduler.FromCurrentSynchronizationContext());


            SystemEvents.SessionSwitch += new SessionSwitchEventHandler(OnSessionSwitch);
            SystemEvents.PowerModeChanged += new PowerModeChangedEventHandler(OnPowerModeChanged);

            _deviceStateServer = new CustomNamedPipeServer(Common.Constants.Daemon + "DeviceState", IpcCommands.SynDeviceState,
                () => HidDevice.EnumerateDevices());
        }

        private void CleanupEdgeClickHook(LowLevelMessage message, ref bool handled)
        {
            if ((message.Message == 0x201 || message.Message == 0x202) &&
                !_suppressPointerMotion && !(_edgeClickGate?.NeedsHook ?? false) &&
                _hookDrawingButton == MouseActions.None && !EdgeInputDiagnostics.Enabled)
                UpdateMouseHookState("EdgeClickPairCompleted");
        }

        private void RecordDiagnosticMouseMessage(LowLevelMessage message, ref bool handled)
        {
            if (message is not LowLevelMouseMessage mouse) return;
            var name = mouse.Message switch
            {
                0x201 => "LeftDown", 0x202 => "LeftUp",
                0x204 => "RightDown", 0x205 => "RightUp",
                0x207 => "MiddleDown", 0x208 => "MiddleUp",
                0x20B => "XDown", 0x20C => "XUp", _ => null
            };
            if (name == null) return;
            EdgeInputDiagnostics.Record("MOUSE", $"Event={name} Injected={(mouse.Flags & 1) != 0} Flags=0x{mouse.Flags:X} HookTime={mouse.Time} Point={mouse.Point.X},{mouse.Point.Y} HookHandled={handled} PointerLock={_suppressPointerMotion}");
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
                _edgeClickGate?.End();
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
            bool flag = _hookDrawingButton != MouseActions.None || _suppressPointerMotion || EdgeInputDiagnostics.Enabled || (_edgeClickGate?.NeedsHook ?? false);
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
                RequestRecovery("PowerResume");
            }
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            // We need to handle sleeping(and other related events)
            // This is so we never lose the lock on the touchpad hardware.
            switch (e.Reason)
            {
                case SessionSwitchReason.RemoteConnect:
                case SessionSwitchReason.SessionLogon:
                case SessionSwitchReason.SessionUnlock:
                    RequestRecovery("SessionRestored");
                    break;
                default:
                    break;
            }
        }

        internal void ResetRawInputState() => _messageWindow.ResetInputState();

        internal void RefreshNativeInput(string reason)
        {
            if (disposedValue) return;
            _messageWindow.ResetInputState();
            _messageWindow.UpdateRegistration();
            LowLevelMouseHook.Unhook();
            UpdateMouseHookState(reason);
            _keyboardHook.Unhook();
            _keyboardHook.StartHook();
        }

        internal async void RequestRecovery(string reason)
        {
            await Task.Delay(600).ConfigureAwait(false);
            _ownerContext.Post(_ =>
            {
                if (disposedValue) return;
                try { PointCapture.Instance.RecoverInput(reason); }
                catch (Exception ex) { Logging.LogMessage("Input recovery failed. Reason=" + reason); Logging.LogException(ex); }
            }, null);
        }

        private async void UpdateDeviceState()
        {
            if (Interlocked.Exchange(ref _stateUpdating, 1) != 0) return;
            await Task.Delay(600).ConfigureAwait(false);
            _ownerContext.Post(_ =>
            {
                try { if (!disposedValue) _messageWindow.UpdateRegistration(); }
                catch (Exception ex) { Logging.LogException(ex); }
                finally { Interlocked.Exchange(ref _stateUpdating, 0); }
            }, null);
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
