using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using ManagedWinapi.Hooks;

internal static class Program
{
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    [STAThread]
    private static void Main()
    {
        // Isolate the real hook lifecycle and callback without registering HID
        // devices, changing user settings or starting another gesture daemon.
        var type = typeof(PointEventTranslator).Assembly.GetType("GestureSign.Daemon.Input.InputProvider");
        var provider = RuntimeHelpers.GetUninitializedObject(type);
        GC.SuppressFinalize(provider);
        var hook = new LowLevelMouseHook();
        Set(provider, "LowLevelMouseHook", hook);
        Set(provider, "_hookDrawingButton", MouseActions.None);
        var translator = (PointEventTranslator)RuntimeHelpers.GetUninitializedObject(typeof(PointEventTranslator));
        Set(translator, "_inputProvider", provider);
        Set(translator, "_pressedMouseButton", new HashSet<MouseActions>());
        Set(translator, "_activeMouseDrawingButton", MouseActions.None);
        try
        {
            Assert(!(bool)Call(provider, "get_SuppressPointerMotion"), "normal input starts unlocked");
            Assert(!Move(translator, 0), "normal motion passes through");
            Assert((bool)Call(provider, "BeginPointerMotionSuppression", Point.Empty), "edge capture installs hook with mouse gestures disabled");
            Assert(hook.Hooked && Move(translator, 0), "physical pointer motion is suppressed during edge capture");
            Assert(!Move(translator, 1), "injected action motion is preserved");
            Assert((int)Get(provider, "_suppressedPointerMoveCount") == 1, "only suppressed physical moves are counted");
            Call(provider, "BeginPointerMotionSuppression", Point.Empty);
            Assert((int)Get(provider, "_suppressedPointerMoveCount") == 1, "repeated begin is idempotent");
            Call(provider, "EndPointerMotionSuppression", "PointUp");
            Assert(!hook.Hooked && !Move(translator, 0), "release restores pointer motion and removes temporary hook");
            Call(provider, "EndPointerMotionSuppression", "CaptureReset");
            Assert(!hook.Hooked, "repeated release is safe");

            Set(provider, "_hookDrawingButton", MouseActions.Right);
            Call(provider, "BeginPointerMotionSuppression", Point.Empty);
            Call(provider, "EndPointerMotionSuppression", "CaptureReset");
            Assert(hook.Hooked && !Move(translator, 0), "cancel preserves an existing mouse gesture hook while unlocking motion");
            Set(provider, "_hookDrawingButton", MouseActions.None);
            Call(provider, "UpdateMouseHookState", "TestCleanup");
            Assert(!hook.Hooked, "temporary hook fully cleaned up");
            Set(provider, "disposedValue", true);
            Assert(!(bool)Call(provider, "BeginPointerMotionSuppression", Point.Empty) &&
                !(bool)Call(provider, "get_SuppressPointerMotion"), "failed begin leaves pointer unlocked");
        }
        finally { hook.Unhook(); }
    }

    private static bool Move(PointEventTranslator translator, int flags)
    {
        var message = new LowLevelMouseMessage(0x200, default, 0, flags, 0, IntPtr.Zero);
        var args = new object[] { message, false };
        typeof(PointEventTranslator).GetMethod("LowLevelMouseHook_MouseMove", Members).Invoke(translator, args);
        return (bool)args[1];
    }

    private static void Set(object instance, string name, object value) => instance.GetType().GetField(name, Members).SetValue(instance, value);
    private static object Get(object instance, string name) => instance.GetType().GetField(name, Members).GetValue(instance);
    private static object Call(object instance, string name, params object[] args) => instance.GetType().GetMethod(name, Members).Invoke(instance, args);
    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}
