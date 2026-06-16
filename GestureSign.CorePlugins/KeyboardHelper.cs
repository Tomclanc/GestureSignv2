using GestureSign.Common.Localization;
using ManagedWinapi;
using ManagedWinapi.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using WindowsInput;
using WindowsInput.Native;

namespace GestureSign.CorePlugins
{
    public class KeyboardHelper
    {
        private static readonly Keys[] StickyModifierKeys =
        {
            Keys.LControlKey,
            Keys.RControlKey,
            Keys.ControlKey,
            Keys.LMenu,
            Keys.RMenu,
            Keys.Menu,
            Keys.LShiftKey,
            Keys.RShiftKey,
            Keys.ShiftKey,
            Keys.LWin,
            Keys.RWin
        };

        public static void ResetKeyState(SystemWindow targetWindow, params Keys[] keys)
        {
            if (keys == null || keys.Length == 0)
                return;

            var shieldWindow = new NativeWindow();
            List<Keys> keysToReset = keys
                .Concat(StickyModifierKeys)
                .Distinct()
                .ToList();
            List<KeyboardKey> keyList = new List<KeyboardKey>(keysToReset.Select(k => new KeyboardKey(k)));
            List<KeyboardKey> failureList = new List<KeyboardKey>(keyList);

            try
            {
                shieldWindow.CreateHandle(new CreateParams() { ExStyle = (int)(WindowExStyleFlags.TOOLWINDOW | WindowExStyleFlags.LAYERED) });

                InputSimulator simulator = new InputSimulator();
                SystemWindow.ForegroundWindow = new SystemWindow(shieldWindow.Handle)
                {
                    WindowState = FormWindowState.Normal
                };

                for (int i = 0; i < keysToReset.Count; i++)
                {
                    if (!Enum.IsDefined(typeof(VirtualKeyCode), keysToReset[i].GetHashCode())) continue;
                    simulator.Keyboard.KeyUp((VirtualKeyCode)keysToReset[i]).Sleep(10);

                    if (keyList[i].IsGloballyPressed)
                    {
                        keyList[i].Release();
                        Thread.Sleep(10);
                        if (!keyList[i].IsGloballyPressed)
                            failureList.Remove(keyList[i]);
                    }
                    else failureList.Remove(keyList[i]);
                }
            }
            catch (Exception)
            {
            }
            finally
            {
                if (targetWindow != null)
                    SystemWindow.ForegroundWindow = targetWindow;
                shieldWindow.DestroyHandle();

                if (failureList.Count != 0)
                {
                    string keysName = string.Join("+ ", keyList.Select(k => k.KeyName));
                    string message = string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.FailureMessage"), keysName);
                    string failurekeysName = string.Join(", ", failureList.Select(k => k.KeyName));
                    message += "\r\n" + string.Format(LocalizationProvider.Instance.GetTextValue("CorePlugins.HotKey.ResetFailure"), failurekeysName);
                    MessageBox.Show(message, LocalizationProvider.Instance.GetTextValue("Messages.Error"),
                        MessageBoxButtons.OK, MessageBoxIcon.Error, MessageBoxDefaultButton.Button1, MessageBoxOptions.DefaultDesktopOnly);
                }
            }
        }
    }
}
