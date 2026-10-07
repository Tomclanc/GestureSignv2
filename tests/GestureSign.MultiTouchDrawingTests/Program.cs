using System.Drawing;
using System.Reflection;
using System.Text.Json.Nodes;
using GestureSign.WinUI;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins.MouseActions;
using GestureSign.Common.Gestures;
using GestureSign.PointPatterns;
using System.Runtime.CompilerServices;
using GestureSign.Foundation.Intent;

int checks = 0;
void Check(bool result, string name) { if (!result) throw new Exception(name); checks++; }
var strokes = new List<List<(double X, double Y)>>();
var drawing = new GestureDrawingSession(strokes);
// Pointer IDs and screen X positions deliberately disagree with touchdown order.
drawing.Begin(90, 400, 100);
drawing.Begin(2, 200, 100);
drawing.Move(90, 480, 100);
drawing.Move(2, 120, 100);
Check(strokes.Count == 2 && drawing.ActiveCount == 2, "Second finger does not erase the first");
Check(strokes[0][^1].X > strokes[0][0].X && strokes[1][^1].X < strokes[1][0].X, "Opposite directions remain independent");
drawing.End(90);
Check(drawing.ActiveCount == 1 && drawing.Move(2, 100, 100), "Releasing the first finger keeps the second drawing");
Check(!drawing.Move(90, 500, 100), "Released contact cannot append points");
drawing.Move(2, 99, 100, finalPoint: true);
drawing.End(2);
Check(strokes[1][^1].X == 99, "Final release point is retained");
var captured = drawing.Snapshot();
Check(captured.Count == 2 && captured[0][0].X == 400, "Snapshot preserves touchdown order");
// Exercise actual configuration serialization, including updating an old template.
var root = Path.Combine(Path.GetTempPath(), "GestureSign-multitouch-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var gestures = new JsonArray();
var factory = typeof(LegacyDataStore).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(JsonArray), typeof(JsonArray), typeof(string)], null)!;
var store = (LegacyDataStore)factory.Invoke([new JsonArray(), gestures, root]);
void Set(string property, string value) => typeof(LegacyDataStore).GetProperty(property)!.SetValue(store, value);
Set("RoamingPath", root); Set("LocalPath", root); Set("GesturesPath", Path.Combine(root, "Gestures.gest"));
store.AddGestureFromPointPatterns("Opposing fingers", captured);
var serialized = JsonNode.Parse(File.ReadAllText(store.GesturesPath!))!.AsArray()[0]!["PointPatterns"]![0]!["Points"]!.AsArray();
Check(serialized.Count == 2 && serialized[0]![0]!.GetValue<string>() == "200, 100" && serialized[1]![0]!.GetValue<string>() == "400, 100", "Saved configuration orders both real starts by position");
Check(serialized[0]![1]!.GetValue<string>() == "120, 100" && serialized[1]![1]!.GetValue<string>() == "480, 100", "Save does not clone one direction across fingers");
Check(gestures[0]!["PointPatterns"]![0]!["OrderByStartPosition"]!.GetValue<bool>(), "New templates declare their ordering independent of capture preferences");
var gesture = new LegacyGesture { Name = "Opposing fingers", Source = gestures[0]!.AsObject() };
store.UpdateGesturePointPatterns(gesture, captured);
Check(gestures[0]!["PointPatterns"]![0]!["Points"]!.AsArray().Count == 2, "Retraining retains both real strokes");
Check(gestures[0]!["PointPatterns"]![0]!["OrderByStartPosition"]!.GetValue<bool>(), "Retraining upgrades template ordering");

// Exercise the actual recognizer, including old files and the non-default capture preference.
var manager = (GestureManager)RuntimeHelpers.GetUninitializedObject(typeof(GestureManager));
void Field(string name, object value) => typeof(GestureManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, value);
Field("gestureAnalyzer", new PointPatternAnalyzer());
Field("_intentCorrections", Array.Empty<IntentGestureCorrection>());
var match = typeof(GestureManager).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
    .Single(m => m.Name == "GetGestureSetNameMatch" && m.GetParameters().Length == 6);
string? Recognize(Point[][] trace, List<IGesture> library, bool location, int level = 0)
    => (string?)match.Invoke(manager, [trace, library, level, null, 80, location]);
var left = Enumerable.Range(0, 11).Select(i => new Point(100 + i * 30, 100)).ToArray();
var right = Enumerable.Range(0, 11).Select(i => new Point(300 - i * 30, 100)).ToArray();
var oldPattern = new PointPattern(new[] { right, left });
var library = new List<IGesture> { new Gesture("Inward", new[] { oldPattern }) };
Check(Recognize([left, right], library, true) == "Inward", "Old right-first templates match left-first captures without retraining");
Check(Recognize([right, left], library, true) == "Inward", "Crossing fingers are ordered by start, not end position");
Check(ReferenceEquals(oldPattern.Points[0], right), "Recognition never mutates old templates or captured contact identities");
Check(Recognize([left, right], library, false) == null && Recognize([right, left], library, false) == "Inward", "Legacy contact-order preference is retained");
oldPattern.OrderByStartPosition = true;
Check(Recognize([left, right], library, false) == "Inward", "New template works with contact-order capture preference");
var outward = new PointPattern(new[] {
    Enumerable.Range(0, 11).Select(i => new Point(100 - i * 30, 100)).ToArray(),
    Enumerable.Range(0, 11).Select(i => new Point(300 + i * 30, 100)).ToArray() }) { OrderByStartPosition = true };
library.Add(new Gesture("Outward", new[] { outward }));
Check(Recognize([left, right], library, true) == "Inward" && Recognize(outward.Points.Reverse().ToArray(), library, true) == "Outward", "Opposite spatial gestures remain distinct rather than arbitrarily permuting fingers");
Field("_Gestures", library);
Check(manager.PreviewGestureName([right, left], ["Inward"]) == "Inward", "Live shape guard uses the same spatial correspondence");
Check(manager.PreservesRequiredDirectionalRetrace("Inward", [left, right]), "Final shape guard accepts the reordered gesture");
var staged = new List<IGesture> { new Gesture("Staged", new[] { oldPattern, outward }) };
Check(Recognize(outward.Points.Reverse().ToArray(), staged, false, 1) == "Staged", "Later stages normalize independently");
var vertical = new[] { new[] { new Point(50, 200) }, new[] { new Point(50, 100) }, Array.Empty<Point>() };
Check(PointPattern.ForComparison(vertical, true)[0][0].Y == 100 && PointPattern.ForComparison(vertical, true)[2].Length == 0, "Same-X starts use Y; empty strokes remain last");
var roundTrip = GestureManager.LoadGesturesFromFile(store.GesturesPath!, true)[0].PointPatterns[0];
Check(roundTrip.OrderByStartPosition && roundTrip.Points[0][0].X == 200, "Daemon deserializes the actual saved ordering marker and points");
var oldFile = Path.Combine(root, "old.gest");
File.WriteAllText(oldFile, "[{\"Name\":\"Old\",\"PointPatterns\":[{\"Points\":[[\"300, 100\"],[\"100, 100\"]]}]}]");
var legacyRoundTrip = GestureManager.LoadGesturesFromFile(oldFile, true)[0].PointPatterns[0];
Check(!legacyRoundTrip.OrderByStartPosition, "Files without marker retain legacy preference semantics");
foreach (var count in new[] { 3, 5 })
{
    var paths = Enumerable.Range(0, count).Select(n => Enumerable.Range(0, 11)
        .Select(i => new Point(100 + n * 100 + (n % 2 == 0 ? i * 5 : -i * 5), 100 + i * 10)).ToArray()).ToArray();
    var template = new PointPattern(paths.Reverse().ToArray()) { OrderByStartPosition = true };
    Check(Recognize(paths, [new Gesture("Many", [template])], false) == "Many", $"{count}-finger independent paths match regardless of touchdown order");
}
var loop = new[] { new Point(300, 100), new Point(360, 100), new Point(360, 200), new Point(300, 200), new Point(300, 100) };
var straight = Enumerable.Range(0, 11).Select(i => new Point(100, 100 + i * 10)).ToArray();
var mixed = new PointPattern(new[] { loop, straight }) { OrderByStartPosition = true };
Field("_Gestures", new List<IGesture> { new Gesture("Mixed", [mixed]) });
Check(manager.PreviewGestureName([straight, loop], ["Mixed"]) == "Mixed", "Preview aligns a loop and a straight finger before shape guards");
Check(manager.PreservesRequiredDirectionalRetrace("Mixed", [straight, loop]), "Final guard aligns each distinct finger before checking retraces");
Check(!manager.PreservesRequiredDirectionalRetrace("Mixed", [straight, straight.Select(p => new Point(300, p.Y)).ToArray()]), "Missing required retrace is still rejected after ordering");
manager.GetTemplateEvidence("Mixed", [straight, loop], out var missingTurn);
Check(!missingTurn, "Template diagnostics align distinct strokes consistently");
var reorderedFile = Path.Combine(root, "marker-first.gest");
File.WriteAllText(reorderedFile, "[{\"Name\":\"Stages\",\"PointPatterns\":[{\"OrderByStartPosition\":true,\"Points\":[[\"10, 20\"]]},{\"Points\":[[\"30, 40\"]],\"OrderByStartPosition\":true}]}]");
Check(GestureManager.LoadGesturesFromFile(reorderedFile, true)[0].PointPatterns.All(p => p.OrderByStartPosition), "Loader accepts marker before or after points in every stage");
var daemonSaved = Path.Combine(root, "daemon-save.gest");
GestureSign.Common.Configuration.FileManager.SaveObject(new[] { new Gesture("Saved", [mixed]) }, daemonSaved, throwException: true);
Check(GestureManager.LoadGesturesFromFile(daemonSaved, true)[0].PointPatterns[0].OrderByStartPosition, "Daemon save/reload retains spatial-order marker");
drawing.Begin(3, 0, 0);
Check(strokes.Count == 1 && captured.Count == 2, "A new gesture clears only the previous session; snapshot remains stable");
drawing.Begin(8, 20, 0);
drawing.Move(8, 80, 0);
Check(drawing.Snapshot().Count == 2 && drawing.Snapshot()[0].Count == 1, "Stationary anchor retained beside a moving finger");
drawing.Clear();
Check(strokes.Count == 0 && drawing.ActiveCount == 0 && !drawing.Move(8, 100, 0), "Clear/cancel removes stale pointer ownership");
for (uint i = 0; i < 5; i++) drawing.Begin(100 - i, i * 50, 0);
Check(strokes.Count == 5, "Five simultaneous contacts stay separate");
Check(drawing.Begin(100, 999, 999) == strokes[0] && strokes.Count == 5, "Repeated pressed event does not duplicate a contact");
// Test the existing runtime order -> FirstDown -> mouse-action reference pipeline.
var orderMethod = typeof(PluginManager).GetMethod("OrderByContactOrder", BindingFlags.NonPublic | BindingFlags.Static)!;
List<T> Order<T>(List<T> values, List<int> identifiers, List<int> order)
    => (List<T>)orderMethod.MakeGenericMethod(typeof(T)).Invoke(null, [values, identifiers, order])!;
var firstFinger = new List<Point> { new(600, 200), new(600, 200) };
var secondFinger = new List<Point> { new(200, 200), new(200, 200) };
var recognitionOrder = new List<List<Point>> { secondFinger, firstFinger };
var actualOrder = Order(recognitionOrder, [2, 90], [90, 2]);
Check(actualOrder[0][0] == firstFinger[0], "First finger is determined by touchdown, not leftmost X or recognition order");
var plugin = new MouseActionsPlugin();
Check(plugin.Deserialize("{\"MouseAction\":\"RightButtonClick\",\"ActionLocation\":\"FirstDown\",\"MoveDurationMilliseconds\":0}"), "Existing right-click configuration deserializes");
var info = new PointInfo(actualOrder.Select(p => p[0]).ToList(), actualOrder, null!, null!);
var reference = typeof(MouseActionsPlugin).GetMethod("GetReferencePoint", BindingFlags.Instance | BindingFlags.NonPublic)!;
Check((Point)reference.Invoke(plugin, [ClickPositions.FirstDown, info])! == firstFinger[0], "Right click targets the first finger's start position");
var repo = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
var mouseSource = File.ReadAllText(Path.Combine(repo, "GestureSign.CorePlugins/MouseActions/MouseActionsPlugin.cs"));
Check(mouseSource.IndexOf("MoveCursor(referencePoint, _settings.MoveDurationMilliseconds)", StringComparison.Ordinal) < mouseSource.IndexOf("clickMethod.Invoke(simulator.Mouse, null)", StringComparison.Ordinal), "Move cursor occurs before sending the mouse button action");
var windowSource = File.ReadAllText(Path.Combine(repo, "GestureSign.WinUI/MainWindow.xaml.cs"));
var drawSource = windowSource[windowSource.IndexOf("private async Task DrawGestureAsync(")..windowSource.IndexOf("private FrameworkElement NewInlineGestureDrawingPanel")];
Check(!drawSource.Contains("AddGestureFromPoints(") && drawSource.Contains("UpdateGesturePointPatterns("), "Standalone drawing saves actual point patterns, not replicated templates");
Check(drawSource.Contains("recognitionDisabled == false") && drawSource.Contains("finally") && drawSource.Contains("if (restoreRecognition)"), "Drawing restores recognition only when it paused an enabled daemon");
Console.WriteLine($"PASS: {checks} multitouch drawing, persistence and first-finger right-click checks");
