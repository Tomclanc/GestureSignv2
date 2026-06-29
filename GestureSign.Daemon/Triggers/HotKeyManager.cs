using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Daemon.Input;
using ManagedWinapi;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GestureSign.Daemon.Triggers
{
    class HotKeyManager : Trigger
    {
        private List<KeyValuePair<Hotkey, List<IAction>>> _hotKeyMap = new List<KeyValuePair<Hotkey, List<IAction>>>();
        private Hotkey _openSettingsHotKey;
        private Hotkey _kandoHotKey;

        public HotKeyManager()
        {
            PointCapture.Instance.ForegroundApplicationsChanged += Instance_ForegroundApplicationsChanged;
            PointCapture.Instance.ModeChanged += Instance_ModeChanged;
            AppConfig.ConfigChanged += AppConfig_ConfigChanged;

            var hotKeyActions = ApplicationManager.Instance.GetApplicationFromWindow(SystemWindow.ForegroundWindow).Where(app => !(app is IgnoredApp)).SelectMany(app => app.Actions).Where(IsStandaloneHotKeyAction).ToList();
            hotKeyActions.AddRange(ApplicationManager.Instance.GetGlobalApplication().Actions.Where(IsStandaloneHotKeyAction));

            if (hotKeyActions.Count != 0)
                RegisterHotKeys(hotKeyActions);
            RegisterOpenSettingsHotKey();
            RegisterKandoHotKey();
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
            }
            else
            {
                RegisterForegroundHotKeys();
                RegisterKandoHotKey();
            }
            RegisterOpenSettingsHotKey();
        }

        private void AppConfig_ConfigChanged(object sender, EventArgs e)
        {
            RegisterOpenSettingsHotKey();
            if (PointCapture.Instance.Mode != Common.Input.CaptureMode.UserDisabled)
                RegisterKandoHotKey();
            else
                UnloadKandoHotKey();
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

                if (h != null && h.ModifierKeys != 0 && h.KeyCode != 0)
                {
                    int index = _hotKeyMap.FindIndex(p => p.Key.KeyCode == h.KeyCode && p.Key.ModifierKeys == h.ModifierKeys);
                    if (index >= 0)
                    {
                        var actionList = _hotKeyMap[index].Value;
                        if (!actionList.Contains(action))
                            actionList.Add(action);
                    }
                    else
                    {
                        var hotKey = new Hotkey() { KeyCode = h.KeyCode, ModifierKeys = h.ModifierKeys };
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
            if (!TryParseHotKey(AppConfig.OpenSettingsHotKey, out keyCode, out modifierKeys))
                return;

            _openSettingsHotKey = new Hotkey { KeyCode = keyCode, ModifierKeys = modifierKeys };
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
            if (PointCapture.Instance.Mode == Common.Input.CaptureMode.UserDisabled || !AppConfig.KandoEnabled || !TryParseHotKey(AppConfig.KandoHotKey, out keyCode, out modifierKeys))
                return;

            _kandoHotKey = new Hotkey { KeyCode = keyCode, ModifierKeys = modifierKeys };
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

        private static bool TryParseHotKey(string settings, out int keyCode, out int modifierKeys)
        {
            keyCode = 0;
            modifierKeys = 0;
            if (string.IsNullOrWhiteSpace(settings))
                return false;

            try
            {
                keyCode = ParseFirstKeyCode(settings);
                if (ParseBool(settings, "Alt"))
                    modifierKeys |= 1;
                if (ParseBool(settings, "Control"))
                    modifierKeys |= 2;
                if (ParseBool(settings, "Shift"))
                    modifierKeys |= 4;
                if (ParseBool(settings, "Windows"))
                    modifierKeys |= 8;
                return keyCode != 0 && modifierKeys != 0;
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
            TrayManager.StartControlPanel();
        }

        private void KandoHotKey_HotkeyPressed(object sender, EventArgs e)
        {
            KandoLauncher.ShowMenu();
        }

        private void Hotkey_HotkeyPressed(object sender, EventArgs e)
        {
            Hotkey hotkey = (Hotkey)sender;
            int index = _hotKeyMap.FindIndex(p => p.Key.KeyCode == hotkey.KeyCode && p.Key.ModifierKeys == hotkey.ModifierKeys);
            if (index >= 0)
            {
                var window = ApplicationManager.Instance.GetForegroundApplications();
                OnTriggerFired(new TriggerFiredEventArgs(_hotKeyMap[index].Value, window.Rectangle.Location));
            }
        }
    }
}
