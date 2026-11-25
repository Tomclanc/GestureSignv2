using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Surface;
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
        var data = Directory.CreateTempSubdirectory("GestureSign-EdgeCursorTest-");
        File.WriteAllText(Path.Combine(data.FullName, "GestureSign.config"), """
            <configuration><appSettings>
              <add key="VisualFeedbackWidth" value="9" />
              <add key="ShowGestureActionHint" value="True" />
            </appSettings></configuration>
            """);
        Environment.SetEnvironmentVariable("GESTURESIGN_STARTUP_TEST_DATA", data.FullName);
        TestHintDoesNotReopenOnClick();
        TestBrightnessAndWin();
        TestTouchPadEdgeFallback();
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

    private static void TestHintDoesNotReopenOnClick()
    {
        using var surface = new SurfaceForm();
        var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        var start = new Point(screen.Left + screen.Width / 2, screen.Top + screen.Height / 2);
        var origin = new List<Point> { start };
        var click = new List<List<Point>> { origin };
        var stroke = new List<List<Point>> { new List<Point> { start, new Point(start.X, start.Y - 85) } };

        var gesture = surface.StartDrawing(origin);
        surface.ShowLiveGestureHint(stroke, "Copy", gesture);
        Assert(surface.Visible, "an actual gesture preview shows the action label");
        surface.EndDrawing(gesture);
        Assert(!surface.Visible, "gesture release hides the previous action label");

        var rightClick = surface.StartDrawing(origin);
        surface.DrawPoints(click, rightClick);
        surface.ShowLiveGestureHint(stroke, "Copy", gesture);
        Assert(!surface.Visible, "stationary right-click cannot reopen the previous gesture surface");
        surface.EndDrawing(rightClick);

        var nextGesture = surface.StartDrawing(origin);
        surface.DrawPoints(stroke, nextGesture);
        Assert(surface.Visible, "the first real trail frame still opens the gesture surface");
        surface.EndDrawing(nextGesture);
    }

    private static void TestTouchPadEdgeFallback()
    {
        var type = typeof(PointEventTranslator).Assembly.GetType("GestureSign.Daemon.Triggers.TouchPadEdgeTrigger");
        var trigger = RuntimeHelpers.GetUninitializedObject(type);
        Set(trigger, "_sourceDevice", Devices.TouchPad);
        Set(trigger, "_gesturePrefix", "TouchPadEdge");
        string Match(params List<Point>[] strokes) => (string)Call(trigger, "GetFallbackEdgeGestureName",
            new PointsCapturedEventArgs(new List<List<Point>>(strokes), new List<Point>()));
        var screen = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
        var edge = new Point(screen.Left + screen.Width / 2, screen.Bottom - 1);
        var interior = new Point(edge.X, screen.Top + screen.Height / 2);
        var edgeStroke = new List<Point> { edge };
        var interiorStroke = new List<Point> { interior, edge };

        Assert(Match(edgeStroke) ==
            "TouchPadEdge.Bottom", "single-finger tap at the bottom edge remains eligible");
        Assert(Match(edgeStroke, interiorStroke) == null,
            "two-finger gesture cannot trigger the bottom edge from its first stroke");
        Assert(Match(interiorStroke) == null,
            "moving onto the edge does not count as an edge-origin gesture");

        // Recreate the reported left-edge Win+Tab binding with one nearly
        // stationary finger on the edge and another scrolling inside it.
        Set(trigger, "_maxTapTravel", 35);
        var leftEdge = new Point(screen.Left + 1, screen.Top + screen.Height / 2);
        var leftEdgeStroke = new List<Point> { leftEdge, new Point(leftEdge.X, leftEdge.Y - 1) };
        var scrollStroke = new List<Point> { interior, new Point(interior.X, screen.Top + screen.Height / 4) };
        Assert(Match(leftEdgeStroke) ==
            "TouchPadEdge.Left", "a single near-stationary contact still matches the left-edge tap binding");
        Assert(Match(leftEdgeStroke, scrollStroke) == null,
            "two-finger scrolling with a nearly stationary left-edge contact cannot match the left-edge tap");
        Assert(Match(scrollStroke, leftEdgeStroke) == null,
            "reversing contact order still cannot match the left-edge tap");

        var captureType = typeof(PointEventTranslator).Assembly.GetType("GestureSign.Daemon.Input.PointCapture");
        var capture = RuntimeHelpers.GetUninitializedObject(captureType);
        GC.SuppressFinalize(capture);
        Set(capture, "_pointsCaptured", new Dictionary<int, List<Point>> { [1] = leftEdgeStroke });
        var firstContact = new InputPoint(1, leftEdge);
        var remainingFrame = new List<InputPoint> { firstContact };
        var overlappingFrame = new List<InputPoint> { firstContact, new InputPoint(2, interior) };

        Assert(!(bool)Call(capture, "ObserveTouchPadContacts", remainingFrame), "one tracked contact leaves the edge capture eligible");
        Assert((bool)Call(capture, "ObserveTouchPadContacts", overlappingFrame), "an untracked late contact disqualifies the edge capture");
        Call(capture, "ObserveTouchPadContacts", remainingFrame);
        var finalFrame = (PointsCapturedEventArgs)Call(capture, "CreateTouchPadVisualPoints", remainingFrame);
        Assert(finalFrame.Points.Count == 1 && finalFrame.TouchPadHadMultipleContacts,
            "a partial release retains the late contact evidence alongside one stored stroke");
        Assert(Call(trigger, "GetFallbackEdgeGestureName", finalFrame) == null,
            "the remaining single stroke cannot trigger the left-edge tap");
        Call(capture, "ResetCaptureBuffers");
        Assert(!(bool)Get(capture, "_touchPadHadMultipleContacts"), "cleanup clears the contact evidence before the next gesture");
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
