using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Daemon.Input;
using ManagedWinapi;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace GestureSign.Daemon.Triggers
{
    class HotKeyManager : Trigger
    {
        private List<KeyValuePair<Hotkey, List<IAction>>> _hotKeyMap = new List<KeyValuePair<Hotkey, List<IAction>>>();
        private Hotkey _openSettingsHotKey;
        private Hotkey _kandoHotKey;
        private Hotkey _kandoSettingsHotKey;
        private bool _kandoEnabled;

        public HotKeyManager()
        {
            PointCapture.Instance.ForegroundApplicationsChanged += Instance_ForegroundApplicationsChanged;
            PointCapture.Instance.ModeChanged += Instance_ModeChanged;
            AppConfig.ConfigChanged += AppConfig_ConfigChanged;
            _kandoEnabled = AppConfig.KandoEnabled;

            var hotKeyActions = ApplicationManager.Instance.GetApplicationFromWindow(SystemWindow.ForegroundWindow).Where(app => !(app is IgnoredApp)).SelectMany(app => app.Actions).Where(IsStandaloneHotKeyAction).ToList();
            hotKeyActions.AddRange(ApplicationManager.Instance.GetGlobalApplication().Actions.Where(IsStandaloneHotKeyAction));

            if (hotKeyActions.Count != 0)
                RegisterHotKeys(hotKeyActions);
            RegisterOpenSettingsHotKey();
            RegisterKandoHotKey();
            RegisterKandoSettingsHotKey();
        }

        private void Instance_ForegroundApplicationsChanged(object sender, ApplicationChangedEventArgs appsChanged)
        {
            if (PointCapture.Instance.Mode == Common.Input.CaptureMode.UserDisabled)
            {
                UnloadHotKeys();
                return;
            }

            var hotKeyActions = appsChanged.Applications.Where(application => application is UserApp && application.Actions != null).SelectMany(app => app.Actions).Where(IsStandaloneHotKeyAction).ToList();
            hotKeyActions.AddRange(ApplicationManager.Instance.GetGlobalApplication().Actions.Where(IsStandaloneHotKeyAction));

            if (hotKeyActions.Count == 0)
                UnloadHotKeys();
            else
                RegisterHotKeys(hotKeyActions);
        }

        private void Instance_ModeChanged(object sender, Common.Input.ModeChangedEventArgs e)
        {
            if (e.Mode == Common.Input.CaptureMode.UserDisabled)
            {
                UnloadHotKeys();
                UnloadKandoHotKey();
                UnloadKandoSettingsHotKey();
            }
            else
            {
                RegisterForegroundHotKeys();
                RegisterKandoHotKey();
                RegisterKandoSettingsHotKey();
            }
            RegisterOpenSettingsHotKey();
        }

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            var kandoEnabled = AppConfig.KandoEnabled;
            if (kandoEnabled != _kandoEnabled)
            {
                _kandoEnabled = kandoEnabled;
                if (kandoEnabled)
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (AppConfig.KandoEnabled)
                            KandoLauncher.StartIfEnabled();
                    });
                else
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        if (!AppConfig.KandoEnabled)
                            KandoLauncher.Stop();
                    });
            }

            RegisterOpenSettingsHotKey();
            if (PointCapture.Instance.Mode != Common.Input.CaptureMode.UserDisabled)
            {
                RegisterKandoHotKey();
                RegisterKandoSettingsHotKey();
            }
            else
            {
                UnloadKandoHotKey();
                UnloadKandoSettingsHotKey();
            }
        }

        private void RegisterForegroundHotKeys()
        {
            var hotKeyActions = ApplicationManager.Instance.GetApplicationFromWindow(SystemWindow.ForegroundWindow).Where(app => !(app is IgnoredApp)).SelectMany(app => app.Actions).Where(IsStandaloneHotKeyAction).ToList();
            hotKeyActions.AddRange(ApplicationManager.Instance.GetGlobalApplication().Actions.Where(IsStandaloneHotKeyAction));

            if (hotKeyActions.Count == 0)
                UnloadHotKeys();
            else
                RegisterHotKeys(hotKeyActions);
        }

        private void RegisterHotKeys(List<IAction> actions)
        {
            UnloadHotKeys();
            _hotKeyMap = new List<KeyValuePair<Hotkey, List<IAction>>>();
            foreach (var action in actions)
            {
                var h = action.Hotkey;

                if (h != null && h.KeyCode != 0 && (h.ModifierKeys != 0 || GestureSign.Shortcuts.ShortcutKeys.IsModifier(h.KeyCode)))
                {
                    int index = _hotKeyMap.FindIndex(p => p.Key.Equals(h));
                    if (index >= 0)
                    {
                        var actionList = _hotKeyMap[index].Value;
                        if (!actionList.Contains(action))
                            actionList.Add(action);
                    }
                    else
                    {
                        var hotKey = new Hotkey() { KeyCode = h.KeyCode, ModifierKeys = h.ModifierKeys, ModifierKeyCodes = h.ModifierKeyCodes };
                        hotKey.HotkeyPressed += Hotkey_HotkeyPressed;
                        _hotKeyMap.Add(new KeyValuePair<Hotkey, List<IAction>>(hotKey, new List<IAction>() { action }));
                        try
                        {
                            hotKey.Register();
                        }
                        catch (HotkeyAlreadyInUseException)
                        {
                            hotKey.Unregister();
                        }
                    }
                }
            }
        }

        private static bool IsStandaloneHotKeyAction(IAction action)
        {
            return action != null && action.Hotkey != null && string.IsNullOrWhiteSpace(action.GestureName);
        }

        private void UnloadHotKeys()
        {
            if (_hotKeyMap != null)
                foreach (var hotKeyPair in _hotKeyMap)
                {
                    hotKeyPair.Key.HotkeyPressed -= Hotkey_HotkeyPressed;
                    hotKeyPair.Key.Dispose();
                }
            _hotKeyMap = null;
        }

        private void RegisterOpenSettingsHotKey()
        {
            UnloadOpenSettingsHotKey();
            int keyCode;
            int modifierKeys;
            int[] exactKeys;
            if (!TryParseHotKey(AppConfig.OpenSettingsHotKey, out keyCode, out modifierKeys, out exactKeys))
                return;

            _openSettingsHotKey = new Hotkey { KeyCode = keyCode, ModifierKeys = modifierKeys, ModifierKeyCodes = exactKeys };
            _openSettingsHotKey.HotkeyPressed += OpenSettingsHotKey_HotkeyPressed;
            try
            {
                _openSettingsHotKey.Register();
            }
            catch (HotkeyAlreadyInUseException)
            {
                UnloadOpenSettingsHotKey();
            }
        }

        private void UnloadOpenSettingsHotKey()
        {
            if (_openSettingsHotKey == null)
                return;

            _openSettingsHotKey.HotkeyPressed -= OpenSettingsHotKey_HotkeyPressed;
            _openSettingsHotKey.Dispose();
            _openSettingsHotKey = null;
        }

        private void RegisterKandoHotKey()
        {
            UnloadKandoHotKey();
            int keyCode;
            int modifierKeys;
            int[] exactKeys;
            if (PointCapture.Instance.Mode == Common.Input.CaptureMode.UserDisabled || !AppConfig.KandoEnabled || !TryParseHotKey(AppConfig.KandoHotKey, out keyCode, out modifierKeys, out exactKeys))
                return;

            _kandoHotKey = new Hotkey { KeyCode = keyCode, ModifierKeys = modifierKeys, ModifierKeyCodes = exactKeys };
            _kandoHotKey.HotkeyPressed += KandoHotKey_HotkeyPressed;
            try
            {
                _kandoHotKey.Register();
            }
            catch (HotkeyAlreadyInUseException)
            {
                UnloadKandoHotKey();
            }
        }

        private void UnloadKandoHotKey()
        {
            if (_kandoHotKey == null)
                return;

            _kandoHotKey.HotkeyPressed -= KandoHotKey_HotkeyPressed;
            _kandoHotKey.Dispose();
            _kandoHotKey = null;
        }

        private void RegisterKandoSettingsHotKey()
        {
            UnloadKandoSettingsHotKey();
            int keyCode;
            int modifierKeys;
            int[] exactKeys;
            if (PointCapture.Instance.Mode == Common.Input.CaptureMode.UserDisabled || !AppConfig.KandoEnabled || !TryParseHotKey(AppConfig.KandoSettingsHotKey, out keyCode, out modifierKeys, out exactKeys))
                return;

            _kandoSettingsHotKey = new Hotkey { KeyCode = keyCode, ModifierKeys = modifierKeys, ModifierKeyCodes = exactKeys };
            _kandoSettingsHotKey.HotkeyPressed += KandoSettingsHotKey_HotkeyPressed;
            try
            {
                _kandoSettingsHotKey.Register();
            }
            catch (HotkeyAlreadyInUseException)
            {
                UnloadKandoSettingsHotKey();
            }
        }

        private void UnloadKandoSettingsHotKey()
        {
            if (_kandoSettingsHotKey == null)
                return;

            _kandoSettingsHotKey.HotkeyPressed -= KandoSettingsHotKey_HotkeyPressed;
            _kandoSettingsHotKey.Dispose();
            _kandoSettingsHotKey = null;
        }

        private static bool TryParseHotKey(string settings, out int keyCode, out int modifierKeys, out int[] exactKeys)
        {
            keyCode = 0;
            modifierKeys = 0;
            exactKeys = null;
            if (string.IsNullOrWhiteSpace(settings))
                return false;

            try
            {
                keyCode = ParseFirstKeyCode(settings);
                exactKeys = Newtonsoft.Json.Linq.JObject.Parse(settings)["ModifierKeyCodes"]?.ToObject<int[]>();
                if (ParseBool(settings, "Alt"))
                    modifierKeys |= 1;
                if (ParseBool(settings, "Control"))
                    modifierKeys |= 2;
                if (ParseBool(settings, "Shift"))
                    modifierKeys |= 4;
                if (ParseBool(settings, "Windows"))
                    modifierKeys |= 8;
                return keyCode != 0 && (modifierKeys != 0 || GestureSign.Shortcuts.ShortcutKeys.IsModifier(keyCode));
            }
            catch
            {
                return false;
            }
        }

        private static int ParseFirstKeyCode(string settings)
        {
            var match = Regex.Match(settings, "\"KeyCode\"\\s*:\\s*\\[\\s*(\\d+)");
            return match.Success ? int.Parse(match.Groups[1].Value) : 0;
        }

        private static bool ParseBool(string settings, string key)
        {
            return Regex.IsMatch(settings, "\"" + Regex.Escape(key) + "\"\\s*:\\s*true", RegexOptions.IgnoreCase);
        }

        private void OpenSettingsHotKey_HotkeyPressed(object sender, EventArgs e)
        {
            TrayManager.StartSettings();
        }

        private void KandoHotKey_HotkeyPressed(object sender, EventArgs e)
        {
            KandoLauncher.ShowMenu();
        }

        private void KandoSettingsHotKey_HotkeyPressed(object sender, EventArgs e)
        {
            KandoLauncher.OpenSettings();
        }

        private void Hotkey_HotkeyPressed(object sender, EventArgs e)
        {
            Hotkey hotkey = (Hotkey)sender;
            int index = _hotKeyMap.FindIndex(p => ReferenceEquals(p.Key, hotkey));
            if (index >= 0)
            {
                var window = ApplicationManager.Instance.GetForegroundApplications();
                OnTriggerFired(new TriggerFiredEventArgs(_hotKeyMap[index].Value, window.Rectangle.Location));
            }
        }
    }
}
