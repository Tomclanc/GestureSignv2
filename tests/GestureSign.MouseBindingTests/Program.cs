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

// Gesture exclusions must block both preview and app/global fallback, not the whole application.
var other = Rule("Other"); other.GestureName = "Other";
var ignore = new IgnoredApp { Name = "Only Down", IgnoredGesture = "down", MatchUsing = MatchUsing.All, IsEnabled = true };
manager.Applications.Add(ignore);
var originalGlobal = global.Actions.ToArray(); global.Actions = originalGlobal.Append(other).ToArray();
capture.SourceDevice = Devices.TouchPad;
Check(!manager.GetRecognizedDefinedAction("Down").Any(), "global gesture ignore leaked to global fallback");
Check(!manager.GetRecognizedDefinedAction(a => a.GestureName == "Down").Any(), "ignored gesture still appears in preview");
Check(manager.GetRecognizedDefinedAction("Other").Single() == other, "gesture rule blocked unrelated gesture");
ignore.IsEnabled = false;
Check(manager.GetRecognizedDefinedAction("Down").Any(), "disabled gesture rule remains active");
ignore.IsEnabled = true; ignore.MatchUsing = MatchUsing.WindowTitle; ignore.MatchString = "GestureIgnoreProbe";
var hwnd = ProbeWindow.CreateWindowEx(0, "STATIC", "GestureIgnoreProbe", 0, 0, 0, 10, 10, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
if (hwnd == IntPtr.Zero) throw new Exception("Cannot create hidden test window");
try
{
    Field(manager, "<CaptureWindow>k__BackingField", new ManagedWinapi.Windows.SystemWindow(hwnd));
    Check(!manager.GetRecognizedDefinedAction("Down").Any(), "matching window gesture rule did not apply");
    Check(manager.GetRecognizedDefinedAction("Other").Single() == other, "scoped rule blocked unrelated gesture");
    Check(!manager.GetApplicationFromWindow(new ManagedWinapi.Windows.SystemWindow(hwnd)).OfType<IgnoredApp>().Any(), "gesture-only rule became a whole-app ignore");
    ignore.MatchString = "Unrelated window";
    Check(manager.GetRecognizedDefinedAction("Down").Any(), "window-scoped rule leaked outside its scope");
    ignore.IsRegEx = true; ignore.MatchString = "^GestureIgnore.*$";
    Check(!manager.GetRecognizedDefinedAction("Down").Any(), "regex gesture scope failed");
}
finally { ProbeWindow.DestroyWindow(hwnd); Field(manager, "<CaptureWindow>k__BackingField", null); }
var ignoredJson = JsonConvert.DeserializeObject<IgnoredApp>(JsonConvert.SerializeObject(ignore));
Check(ignoredJson.IgnoredGesture == "down", "backend serialization lost ignored gesture");
Check(JsonConvert.DeserializeObject<IgnoredApp>("{\"Name\":\"Legacy\"}").IgnoredGesture == null, "legacy whole-app rule changed");
manager.Applications.Remove(ignore); global.Actions = originalGlobal;
capture.SourceDevice = Devices.Mouse; capture.MouseGestureButton = MouseActions.Middle;

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
    store.AddAction(uiApp, "Continuous volume", "", 0, 0, 0, "{\"ContactCount\":2,\"Gesture\":4}");
    var continuous = JsonNode.Parse(File.ReadAllText(store.ActionsPath))[0]["Actions"].AsArray().Last().ToJsonString();
    var continuousAction = JsonConvert.DeserializeObject<GestureSign.Common.Applications.Action>(continuous);
    Check(continuousAction.ContinuousGesture.ContactCount == 2 && continuousAction.ContinuousGesture.Gesture == Gestures.Up, "continuous editor data cannot be read by backend");
    store.SaveIgnoredGesture(null, "Down", 4, "", false, true);
    var savedIgnore = JsonNode.Parse(File.ReadAllText(store.ActionsPath)).AsArray().OfType<JsonObject>().Single(a => a["IgnoredGesture"]?.GetValue<string>() == "Down");
    Check(savedIgnore["MatchUsing"].GetValue<int>() == 4, "global gesture ignore not persisted");
    // Use the actual in-memory source node for edit persistence.
    var rootField = typeof(LegacyDataStore).GetField("_actionsRoot", BindingFlags.NonPublic | BindingFlags.Instance);
    var actualIgnore = ((JsonArray)rootField.GetValue(store)).OfType<JsonObject>().Single(a => a["IgnoredGesture"]?.GetValue<string>() == "Down");
    store.SaveIgnoredGesture(new LegacyApplication { Source = actualIgnore }, "Other", 2, "editor.exe", false, false);
    var editedIgnore = JsonNode.Parse(File.ReadAllText(store.ActionsPath)).AsArray().OfType<JsonObject>().Single(a => a["IgnoredGesture"]?.GetValue<string>() == "Other");
    Check(!editedIgnore["IsEnabled"].GetValue<bool>() && editedIgnore["MatchString"].GetValue<string>() == "editor.exe", "edit lost gesture-ignore scope or enabled state");
    File.Delete(backup); // The isolated test can create both archives within the same timestamp second.
    var ignoreBackup = store.CreateBackup();
    File.WriteAllText(store.ActionsPath, "[]");
    store.RestoreArchive(ignoreBackup);
    var restoredIgnore = JsonNode.Parse(File.ReadAllText(store.ActionsPath)).AsArray().OfType<JsonObject>().Single(a => a["IgnoredGesture"]?.GetValue<string>() == "Other");
    Check(!restoredIgnore["IsEnabled"].GetValue<bool>() && restoredIgnore["MatchUsing"].GetValue<int>() == 2 && restoredIgnore["MatchString"].GetValue<string>() == "editor.exe", "backup/restore lost gesture exclusion");

}
finally { Directory.Delete(root, true); }
Console.WriteLine($"PASS: {checks} mouse binding, dispatch, recognition and persistence checks.");

// Extended training payload retains the original point-array prefix for older UI readers.
var ipcWriter = typeof(GestureSign.Common.InterProcessCommunication.NamedPipe).GetMethod("WriteMessage", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
var trainingPoints = new System.Drawing.Point[][][] { new[] { new[] { new System.Drawing.Point(1,2), new System.Drawing.Point(3,4) } } };
byte[] Wire(object payload) { using var stream = new MemoryStream(); ipcWriter.Invoke(null, new object[] { stream, GestureSign.Common.InterProcessCommunication.IpcCommands.GotGesture, payload }); return stream.ToArray(); }
var oldWire = Wire(trainingPoints);
var newWire = Wire(Tuple.Create(trainingPoints, 2, 0));
if (newWire.Length != oldWire.Length + 8 || !oldWire.SequenceEqual(newWire.Take(oldWire.Length))) throw new Exception("Training IPC compatibility changed");
if (BitConverter.ToInt32(newWire, oldWire.Length) != 2 || BitConverter.ToInt32(newWire, oldWire.Length + 4) != 0) throw new Exception("Training input metadata changed");
Console.WriteLine("PASS: training IPC retains legacy point payload and appends exact device/button metadata.");

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

static class ProbeWindow
{
    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
    internal static extern IntPtr CreateWindowEx(int exStyle, string className, string title, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [System.Runtime.InteropServices.DllImport("user32.dll")] internal static extern bool DestroyWindow(IntPtr hwnd);
}
