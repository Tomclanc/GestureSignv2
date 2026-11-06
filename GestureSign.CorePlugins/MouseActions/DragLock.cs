using System;
using System.Runtime.InteropServices;
using System.Threading;
using WindowsInput;
namespace GestureSign.CorePlugins.MouseActions;
public static class DragLock
{
    private static readonly object Gate = new();
    private static Timer timer;
    private static bool held;
    public static event Action<bool> Changed;
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    internal static Func<int, bool> KeyIsDown = key => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static void Toggle()
    {
        lock (Gate)
        {
            if (held) { Release(); return; }
            // Do not take ownership of a button physically held by the user.
            if (KeyIsDown(1)) return;
            new InputSimulator().Mouse.LeftButtonDown();
            held = true;
            timer = new Timer(_ => { if (KeyIsDown(27)) Release(); }, null, 0, 30);
            Changed?.Invoke(true);
        }
    }
    public static void Release()
    {
        lock (Gate)
        {
            if (!held) return;
            try { new InputSimulator().Mouse.LeftButtonUp(); }
            catch (Exception ex) { GestureSign.Common.Log.Logging.LogException(ex); return; }
            held = false;
            timer?.Dispose(); timer = null;
            Changed?.Invoke(false);
        }
    }
}
