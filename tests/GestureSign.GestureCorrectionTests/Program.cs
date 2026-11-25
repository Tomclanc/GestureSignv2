using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using GestureSign.Common.Gestures;
using GestureSign.Common.Input;
using GestureSign.Foundation.Intent;
using GestureSign.PointPatterns;

int checks = 0;
void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
var root = Path.Combine(Path.GetTempPath(), "gesture-corrections-" + Guid.NewGuid().ToString("N"));
var manager = (GestureManager)RuntimeHelpers.GetUninitializedObject(typeof(GestureManager));
void Set(string name, object value) => typeof(GestureManager).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(manager, value);
Set("gestureAnalyzer", new PointPatternAnalyzer());
foreach (int fingers in new[] { 1, 2, 3, 4 })
{
    var sample = new IntentSample { Candidate = "Original", Frames = Enumerable.Range(0, 12).Select(i => new IntentFrame(i * 20, Enumerable.Range(1, fingers).Select(n => new IntentPoint(n, n * 60 + i * 4, i * 10)).ToArray())).ToArray() };
    IntentGestureCorrections.Save(root, sample, "Target");
    IntentGestureCorrections.Save(root, sample, "Target");
    var stored = IntentGestureCorrections.Read(root);
    Check(stored.Count(c => c.SampleId == sample.Id) == 1, "Duplicate correction");
    Check(sample.Candidate == "Original", "Historical candidate changed");
    Set("_intentCorrections", stored); Set("_correctionTemplates", null);
    var original = Enumerable.Range(1, fingers).Select(n => new[] { new Point(n * 60, 0), new Point(n * 60, 100), new Point(n * 60 + 100, 100) }).ToArray();
    var library = new List<IGesture> { new Gesture("Target", new[] { new PointPattern(original) { OrderByStartPosition = fingers % 2 == 0 } }) };
    var captured = Enumerable.Range(1, fingers).Select(n => sample.Frames.Select(f => f.Points.Single(p => p.Contact == n)).Select(p => new Point((int)p.X, (int)p.Y)).ToArray()).ToArray();
    Check(manager.GetGestureSetNameMatch(captured, library, 0, out _) == "Target", "Corrected trace did not participate in recognition");
    Check(library.Count == 1 && library[0].PointPatterns.Length == 1, "Correction changed original/staged template");
    var expanded = (List<IGesture>)typeof(GestureManager).GetMethod("WithCorrections", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, new object[] { library, 0 });
    Check(expanded.Count == 2, "Wrong contact corrections leaked into recognition");
    Check(expanded[1].PointPatterns[0].OrderByStartPosition == library[0].PointPatterns[0].OrderByStartPosition, "Correction lost the original template's ordering policy");
    IntentGestureCorrections.Save(root, sample, null);
    Set("_intentCorrections", IntentGestureCorrections.Read(root)); Set("_correctionTemplates", null);
    expanded = (List<IGesture>)typeof(GestureManager).GetMethod("WithCorrections", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, new object[] { library, 0 });
    Check(expanded.Count == 1, "Undo retained correction template");
}
try { IntentGestureCorrections.Save(root, new IntentSample(), "Bad"); throw new Exception("Invalid trace accepted"); }
catch (ArgumentException) { checks++; }
// Drive the real event handlers without registering raw input or launching the daemon.
Set("_intentCorrections", Array.Empty<IntentGestureCorrection>());
Set("_correctionTemplates", null);
var loaded = GestureManager.LoadGesturesFromFile(Path.Combine(AppContext.BaseDirectory, "Defaults", "Gestures.gest"), throwException: true);
var doubleTap = loaded.Single(g => g.Name == "三指双击");
var singleTap = loaded.Single(g => g.Name == "三指点按");
var rightSwipe = loaded.Single(g => g.Name == "三指右滑");
var tapThenSwipe = new Gesture("Tap then swipe", new[] { doubleTap.PointPatterns[0], rightSwipe.PointPatterns[0] });
Set("_Gestures", new List<IGesture> { doubleTap, singleTap, rightSwipe, tapThenSwipe });
var tap = doubleTap.PointPatterns[0].Points;
var swipe = rightSwipe.PointPatterns[0].Points;
var source = new SyntheticTouchPad();
manager.Load(source);
string Complete(Point[][] points, params string[] actions)
{
    source.Release(points);
    return manager.PreviewCompletedGestureName(points, actions);
}

var pairMatches = 0;
for (var i = 0; i < 3; ++i)
{
    Check(Complete(tap, doubleTap.Name) == null, "First contact release must not be recognized twice as a double tap");
    Check(manager.PreviewCompletedGestureName(tap, new[] { singleTap.Name }) == singleTap.Name,
        "Final action-aware preview should still select a bound single tap");
    Check(manager.PreviewGestureName(tap, new[] { doubleTap.Name }) == doubleTap.Name,
        "Live preview for the following capture should still use the pending second stage");
    if (Complete(tap, doubleTap.Name) == doubleTap.Name) pairMatches++;
}
Check(pairMatches == 3, "Three tap pairs should dispatch exactly three double taps");
Check(Complete(tap, tapThenSwipe.Name) == null, "Tap should only arm tap-then-swipe");
Check(Complete(swipe, tapThenSwipe.Name) == tapThenSwipe.Name, "Swipe should complete an armed tap-then-swipe");
Check(Complete(swipe, rightSwipe.Name) == rightSwipe.Name, "Independent swipes should remain usable");
Check(Complete(tap, doubleTap.Name) == null, "First tap should arm the timeout test");
Set("_lastGestureTime", unchecked(Environment.TickCount - 801));
Check(Complete(tap, doubleTap.Name) == null, "First tap after the existing timeout must not complete an expired sequence");
Console.WriteLine($"PASS: {checks} correction storage, multi-stage, and actual recognizer checks.");

file sealed class SyntheticTouchPad : IPointCapture
{
    public event PointsCapturedEventHandler CaptureStarted;
    public event PointsCapturedEventHandler BeforePointsCaptured;
    event PointsCapturedEventHandler IPointCapture.AfterPointsCaptured { add { } remove { } }
    event PointsCapturedEventHandler IPointCapture.PointCaptured { add { } remove { } }
    event EventHandler IPointCapture.CaptureEnded { add { } remove { } }
    event RecognitionEventHandler IPointCapture.GestureRecognized { add { } remove { } }
    public bool TemporarilyDisableCapture { get; set; }
    public Devices SourceDevice => Devices.TouchPad;
    public CaptureState State { get; set; }
    public CaptureMode Mode { get; set; } = CaptureMode.Normal;

    public void Release(Point[][] points)
    {
        var args = new PointsCapturedEventArgs(points.Select(stroke => stroke.ToList()).ToList(), new List<Point> { points[0][0] });
        CaptureStarted?.Invoke(this, args);
        BeforePointsCaptured?.Invoke(this, args);
    }
}
