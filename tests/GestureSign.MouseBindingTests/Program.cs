using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using GestureSign.Common.Applications;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.PointPatterns;
using GestureSign.WinUI;
using ManagedWinapi.Hooks;
using Newtonsoft.Json;
using GAction = GestureSign.Common.Applications.Action;

int checks = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; }
GAction Rule(string name, MouseActions button = MouseActions.None) => new()
{ Name = name, GestureName = "Down", MouseGestureButton = button, Commands = new[] { new Command { IsEnabled = true } } };
var any = Rule("Legacy");
var left = Rule("Left", MouseActions.Left);
var middle = Rule("Middle", MouseActions.Middle);
var right = Rule("Right", MouseActions.Right);
foreach (var selected in new[] { left, middle, right })
{
    var result = MouseGestureBinding.Select(new[] { any, left, middle, right }, Devices.Mouse, selected.MouseGestureButton);
    Check(result.Count == 1 && result[0] == selected, "wrong button or duplicate Any execution");
    Check(((GAction)selected.DeepCopy()).MouseGestureButton == selected.MouseGestureButton, "copy loses start button");
    var roundtrip = JsonConvert.DeserializeObject<GAction>(JsonConvert.SerializeObject(selected), new JsonSerializerSettings { Converters = { new CommandConverter() } });
    Check(roundtrip.MouseGestureButton == selected.MouseGestureButton, "backend JSON loses button");
}
foreach (var device in new[] { Devices.TouchPad, Devices.TouchScreen, Devices.Pen, Devices.None })
    Check(MouseGestureBinding.Select(new[] { any, left, middle, right }, device, MouseActions.Left).SequenceEqual(new[] { any }), "mouse-only rule leaked to other device");
Check(JsonConvert.DeserializeObject<GAction>("{\"Name\":\"Old\",\"GestureName\":\"Down\"}").MouseGestureButton == MouseActions.None, "old config default changed");
Check(MouseGestureBinding.Select(new[] { any, right }, Devices.Mouse, MouseActions.Middle).Single() == any, "Any fallback lost");
middle.IgnoredDevices = Devices.Mouse;
Check(MouseGestureBinding.Select(new[] { any, middle }, Devices.Mouse, MouseActions.Middle).Single() == any, "ignored mouse rule hides Any fallback");
middle.IgnoredDevices = Devices.None;

// Exercise actual application/global dispatch and preview with mutable capture context.
var manager = (ApplicationManager)RuntimeHelpers.GetUninitializedObject(typeof(ApplicationManager));
void Field(object obj, string name, object value) => obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
Field(manager, "<LoadingTask>k__BackingField", Task.CompletedTask);
var global = new GlobalApp { Actions = new[] { any, middle, right } };
var app = new UserApp { Name = "Test application", Actions = new[] { left } };
Field(manager, "_applications", new List<IApplication> { global, app });
Field(manager, "_recognizedApplication", new[] { app });
var capture = new Capture();
manager.Load(capture);
foreach (var rule in new[] { left, middle, right })
{
    capture.MouseGestureButton = rule.MouseGestureButton;
    Check(manager.GetRecognizedDefinedAction("Down").Single() == rule, "actual dispatch/global fallback selected wrong button");
    Check(manager.GetRecognizedDefinedAction(a => a.GestureName == "Down").Single() == rule, "preview differs from dispatch");
}
capture.SourceDevice = Devices.TouchPad;
Check(manager.GetRecognizedDefinedAction("Down").Single() == any, "touchpad blocked by mouse-only app override");
capture.SourceDevice = Devices.Mouse; capture.MouseGestureButton = MouseActions.Middle;
middle.IsEnabled = false;
Check(manager.GetRecognizedDefinedAction("Down").Single() == any, "disabled specific rule hides fallback");
middle.IsEnabled = true;

// Combination lookup uses the held start button independently of the second input.
var rightLeft = Rule("Right + Left", MouseActions.Right);
rightLeft.GestureName = "";
rightLeft.MouseHotkey = MouseActions.Left;
var middleLeft = Rule("Middle + Left", MouseActions.Middle);
middleLeft.GestureName = "";
middleLeft.MouseHotkey = MouseActions.Left;
var rightWheel = Rule("Right + Wheel", MouseActions.Right);
rightWheel.GestureName = "";
rightWheel.MouseHotkey = MouseActions.WheelForward;
global.Actions = new[] { rightLeft, middleLeft, rightWheel };
app.Actions = Array.Empty<IAction>();
capture.MouseGestureButton = MouseActions.Right;
Check(manager.GetRecognizedDefinedAction(a => a.MouseHotkey == MouseActions.Left).Single() == rightLeft, "right + left combination dispatch");
Check(manager.GetRecognizedDefinedAction(a => a.MouseHotkey == MouseActions.WheelForward).Single() == rightWheel, "right + wheel combination dispatch");
capture.MouseGestureButton = MouseActions.Middle;
Check(manager.GetRecognizedDefinedAction(a => a.MouseHotkey == MouseActions.Left).Single() == middleLeft, "middle + left combination dispatch");
Check(manager.GetRecognizedDefinedAction(a => a.MouseHotkey == MouseActions.WheelForward).Count == 0, "wheel combination leaked to another start button");
app.Actions = new[] { left };

// Same shape, different names: the actual recognition event must use the button's library.
left.GestureName = "LeftDown"; middle.GestureName = "MiddleDown"; right.GestureName = "RightDown";
global.Actions = new[] { middle, right };
typeof(ApplicationManager).GetField("_instance", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, manager);
var recognizer = (GestureManager)RuntimeHelpers.GetUninitializedObject(typeof(GestureManager));
Field(recognizer, "gestureAnalyzer", new PointPatternAnalyzer());
Field(recognizer, "_intentCorrections", Array.Empty<GestureSign.Foundation.Intent.IntentGestureCorrection>());
var points = new[] { new Point(100, 100), new Point(100, 180), new Point(100, 260) };
Field(recognizer, "_Gestures", new List<IGesture>(new[] { left, middle, right }.Select(a => new Gesture(a.GestureName, new[] { new PointPattern(new[] { points }) }))));
var recognize = typeof(GestureManager).GetMethod("PointCapture_BeforePointsCaptured", BindingFlags.NonPublic | BindingFlags.Instance);
foreach (var rule in new[] { left, middle, right })
{
    Field(recognizer, "_gestureLevel", 0);
    capture.MouseGestureButton = rule.MouseGestureButton;
    recognize.Invoke(recognizer, new object[] { capture, new PointsCapturedEventArgs(new List<List<Point>> { points.ToList() }, new List<Point> { points[0] }) });
    Check(recognizer.GestureName == rule.GestureName, "same-shaped mouse gestures chose wrong name");
}

// Exercise the WinUI data store: add, edit, backup/restore preserve the new condition.
var root = Path.Combine(Path.GetTempPath(), "GestureSign-mouse-bindings-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var jsonApp = new JsonObject { ["Name"] = "Test", ["Actions"] = new JsonArray() };
    var factory = typeof(LegacyDataStore).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(JsonArray), typeof(JsonArray), typeof(string) }, null);
    var store = (LegacyDataStore)factory.Invoke(new object[] { new JsonArray(jsonApp), new JsonArray(), root });
    void PathProperty(string name, string path) => typeof(LegacyDataStore).GetProperty(name).SetValue(store, path);
    PathProperty("RoamingPath", root); PathProperty("LocalPath", root);
    PathProperty("ActionsPath", Path.Combine(root, "Actions.gsa"));
    PathProperty("GesturesPath", Path.Combine(root, "Gestures.gest"));
    PathProperty("ConfigPath", Path.Combine(root, "GestureSign.config"));
    var uiApp = new LegacyApplication { Source = jsonApp };
    store.AddAction(uiApp, "MiddleDown", "Down", 0, (int)MouseActions.Middle);
    var jsonRule = jsonApp["Actions"].AsArray()[0].AsObject();
    Check(jsonRule["MouseGestureButton"].GetValue<int>() == (int)MouseActions.Middle, "UI add loses start button");
    store.UpdateAction(new LegacyAction { Source = jsonRule }, "RightDown", "Down", "", true, true, 0, 0, "", "", (int)MouseActions.Right);
    Check(jsonRule["MouseGestureButton"].GetValue<int>() == (int)MouseActions.Right, "UI edit loses start button");
    var backup = store.CreateBackup();
    File.WriteAllText(store.ActionsPath, "[]");
    store.RestoreArchive(backup);
    Check(JsonNode.Parse(File.ReadAllText(store.ActionsPath))[0]["Actions"][0]["MouseGestureButton"].GetValue<int>() == (int)MouseActions.Right, "backup/restore loses start button");
    store.AddAction(uiApp, "Right + Left", "", 0, (int)MouseActions.Right, (int)MouseActions.Left);
    var combination = jsonApp["Actions"].AsArray().Last().AsObject();
    Check(combination["MouseHotkey"].GetValue<int>() == (int)MouseActions.Left && combination["GestureName"].GetValue<string>() == "", "UI add loses combination without a drawn gesture");
}
finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS: {checks} mouse binding, dispatch, recognition and persistence checks.");

sealed class Capture : IPointCapture, IMouseGestureCapture
{
    public Devices SourceDevice { get; set; } = Devices.Mouse;
    public MouseActions MouseGestureButton { get; set; }
    public CaptureMode Mode { get; set; } = CaptureMode.Normal;
    public CaptureState State { get; set; }
    public bool TemporarilyDisableCapture { get; set; }
    public event PointsCapturedEventHandler AfterPointsCaptured;
    public event PointsCapturedEventHandler BeforePointsCaptured;
    public event PointsCapturedEventHandler CaptureStarted;
    public event EventHandler CaptureEnded;
    public event RecognitionEventHandler GestureRecognized;
    public event PointsCapturedEventHandler PointCaptured;
}
