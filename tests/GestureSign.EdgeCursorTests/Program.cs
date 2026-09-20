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

    private static void TestBrightnessAndWin()
    {
        var policy = typeof(PointEventTranslator).Assembly.GetType("GestureSign.Daemon.Triggers.TouchPadEdgeTrigger")
            .GetMethod("IsContinuousEdgeAction", BindingFlags.Static | BindingFlags.NonPublic);
        bool Continuous(string plugin, string json) => (bool)policy.Invoke(null, new object[]
        {
            new GestureSign.Common.Applications.Action { Commands = new[] {
                new GestureSign.Common.Applications.Command { IsEnabled = true, PluginClass = plugin, CommandSettings = json } } }, true
        });
        const string brightness = "GestureSign.CorePlugins.ScreenBrightness.ScreenBrightnessPlugin";
        Assert(!Continuous(brightness, "{\"Method\":0,\"Percent\":10}"), "legacy brightness remains once per swipe");
        Assert(Continuous(brightness, "{\"Method\":0,\"ContinuousEdge\":true}"), "brightness up continuous enabled");
        Assert(Continuous(brightness, "{\"Method\":1,\"ContinuousEdge\":true}"), "brightness down continuous enabled");
        Assert(!Continuous(brightness, "{\"Method\":0,\"ContinuousEdge\":false}"), "brightness continuous can be disabled");
        Assert(!Continuous(brightness, "{\"Method\":2,\"ContinuousEdge\":true}"), "invalid brightness method rejected");
        Assert(Continuous("GestureSign.CorePlugins.Volume.VolumePlugin", "{\"Method\":0}"), "legacy continuous volume preserved");
        var level = typeof(GestureSign.CorePlugins.HotKey.HotKeyPlugin).Assembly.GetType(brightness)
            .GetMethod("SelectBrightnessLevel", BindingFlags.Static | BindingFlags.NonPublic);
        byte Select(int current, int method, int percent) => (byte)level.Invoke(null, new object[] {current, new byte[] {100, 20, 0, 40, 60, 80}, method, percent});
        Assert(Select(40, 0, 2) == 60, "small increase advances on coarse brightness levels");
        Assert(Select(40, 1, 2) == 20, "small decrease advances on coarse brightness levels");
        Assert(Select(100, 0, 5) == 100 && Select(0, 1, 5) == 0, "brightness saturates at supported limits");
        var hotkey = new GestureSign.CorePlugins.HotKey.HotKeyPlugin();
        Assert(hotkey.Deserialize("{\"Windows\":false,\"Control\":false,\"Alt\":false,\"Shift\":false,\"KeyCode\":[91],\"SendByKeybdEvent\":false}"), "standalone Win command deserializes");
        var keys = (GestureSign.CorePlugins.HotKey.HotKeySettings)typeof(GestureSign.CorePlugins.HotKey.HotKeyPlugin)
            .GetField("_Settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hotkey);
        Assert(!keys.Windows && !keys.Control && !keys.Alt && !keys.Shift && keys.KeyCode.Count == 1 && keys.KeyCode[0] == System.Windows.Forms.Keys.LWin,
            "standalone Win uses one normal keypress with no held modifiers");
    }

    [STAThread]
    private static void Main(string[] args)
    {
        if (Array.IndexOf(args, "--brightness-overlay") >= 0) { TestBrightnessOverlay(); return; }
        TestBrightnessAndWin();
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

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static void TestBrightnessOverlay()
    {
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        var type = typeof(GestureSign.CorePlugins.HotKey.HotKeyPlugin).Assembly
            .GetType("GestureSign.CorePlugins.ScreenBrightness.BrightnessOverlay");
        var show = type.GetMethod("ShowBrightness", BindingFlags.Static | BindingFlags.NonPublic);
        var current = type.GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
        void Pump(int ms)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < ms) { System.Windows.Forms.Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        var foreground = GetForegroundWindow();
        try
        {
            show.Invoke(null, new object[] { 25 });
            Pump(100);
            var overlay = (System.Windows.Forms.Form)current.GetValue(null);
            Assert(overlay.Visible && GetForegroundWindow() == foreground, "brightness overlay does not activate");
            Assert(System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea.Contains(overlay.Bounds), "overlay stays inside monitor work area");
            Pump(1000);
            show.Invoke(null, new object[] { 75 });
            Assert(ReferenceEquals(current.GetValue(null), overlay) && overlay.AccessibleDescription == "75%", "continuous brightness updates reuse the overlay");
            Pump(900);
            Assert(overlay.Visible, "updates extend overlay timeout");
            Pump(1100);
            Assert(overlay.IsDisposed && current.GetValue(null) == null, "idle overlay closes and releases its timer");
            show.Invoke(null, new object[] { 120 });
            Assert(((System.Windows.Forms.Form)current.GetValue(null)).AccessibleDescription == "100%", "overlay can reopen with clamped percentage");
        }
        finally { (current.GetValue(null) as System.Windows.Forms.Form)?.Dispose(); }
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
