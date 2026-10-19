using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GestureSign.Shortcuts;
using ManagedWinapi.Hooks;

namespace ManagedWinapi
{
    public partial class Hotkey
    {
        private bool UsesKeyboardHook => ShortcutKeys.IsModifier(KeyCode) || (ModifierKeyCodes?.Length ?? 0) != 0;
        private static readonly List<Hotkey> HookHotkeys = new List<Hotkey>();
        private static LowLevelKeyboardHook sharedHook;
        private readonly ModifierTapState tapState = new ModifierTapState();
        private readonly HashSet<int> swallowed = new HashSet<int>();
        private void UpdateHookRegistration(bool enabled)
        {
            if (enabled && UsesKeyboardHook)
            {
                if (!HookHotkeys.Contains(this))
                {
                    tapState.Reset();
                    for (int key = 8; key <= 165; key++)
                        if (key != 16 && key != 17 && key != 18 && (GetAsyncKeyState(key) & 0x8000) != 0)
                            tapState.Process(key, true, false, KeyCode);
                    tapState.Cancel(); // Do not interpret a key held before registration as a new tap.
                    HookHotkeys.Add(this);
                }
                if (sharedHook == null)
                {
                    sharedHook = new LowLevelKeyboardHook();
                    sharedHook.KeyIntercepted += OnHookKey;
                    sharedHook.StartHook();
                }
            }
            else
            {
                HookHotkeys.Remove(this);
                tapState.Reset();
                if (HookHotkeys.Count == 0 && sharedHook != null)
                {
                    sharedHook.Dispose();
                    sharedHook = null;
                }
            }
        }
        private static void OnHookKey(int msg, int vk, int scan, int flags, int time, IntPtr extra, ref bool handled)
        {
            if ((flags & 16) != 0) return; // Never trigger from commands injected by us or another app.
            bool down = msg == 0x100 || msg == 0x104;
            bool up = msg == 0x101 || msg == 0x105;
            if (!down && !up) return;
            int key = ShortcutKeys.Normalize(vk, scan, flags);
            foreach (var hotkey in HookHotkeys.ToArray())
            {
                bool fire;
                if (ShortcutKeys.IsModifier(hotkey.KeyCode))
                    fire = hotkey.tapState.Process(key, down, false, hotkey.KeyCode);
                else
                {
                    fire = down && key == hotkey.KeyCode && !hotkey.swallowed.Contains(key) &&
                        ShortcutKeys.Matches(hotkey.ModifierKeys, hotkey.ModifierKeyCodes, k => (GetAsyncKeyState(k) & 0x8000) != 0);
                    if (fire) hotkey.swallowed.Add(key);
                    if (key == hotkey.KeyCode && hotkey.swallowed.Contains(key)) handled = true;
                    if (up) hotkey.swallowed.Remove(key);
                }
                if (fire) PostMessage(hotkey.hWnd, WM_HOTKEY, new IntPtr(hotkey._hotkeyIndex), IntPtr.Zero);
            }
        }
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
