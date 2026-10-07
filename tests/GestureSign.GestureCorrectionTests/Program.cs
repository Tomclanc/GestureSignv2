using System.Drawing;
using System.Reflection;
using System.Runtime.CompilerServices;
using GestureSign.Common.Gestures;
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
Console.WriteLine($"PASS: {checks} correction storage and actual recognizer checks.");
