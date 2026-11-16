using System.Drawing;
using GestureSign.Common.Applications;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Triggers;

int checks = 0;
void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
List<Point> Origins(int n) => Enumerable.Range(0, n).Select(i => new Point(100 + i * 100, 200)).ToList();

// All permutations of the moving finger, both axes/directions, two to five
// contacts, and realistic stationary-contact jitter must reject anchored input.
for (int n = 2; n <= 5; n++)
for (int moving = 0; moving < n; moving++)
foreach (bool vertical in new[] { false, true })
foreach (int sign in new[] { -1, 1 })
{
    var start = Origins(n);
    var end = start.Select((p, i) => new Point(p.X + (vertical ? 1 : sign * (i == moving ? 300 : 3)),
        p.Y + (vertical ? sign * (i == moving ? 300 : 3) : 1))).ToList();
    ContinuousGestureTrigger.TryGetTranslation(start, end, out int dx, out int dy);
    Check(Math.Max(Math.Abs(dx), Math.Abs(dy)) <= 3, "Anchors cannot borrow moving finger distance");
    end = start.Select((p, i) => i == moving ? new Point(p.X + sign * 300, p.Y) : p).ToList();
    Check(!ContinuousGestureTrigger.TryGetTranslation(start, end, out _, out _), "Stationary anchors rejected");
}
foreach (bool vertical in new[] { false, true })
foreach (int sign in new[] { -1, 1 })
{
    var start = Origins(3);
    var end = start.Select((p, i) => new Point(p.X + (vertical ? 2 : sign * (50 + i * 20)),
        p.Y + (vertical ? sign * (50 + i * 20) : 2))).ToList();
    Check(ContinuousGestureTrigger.TryGetTranslation(start, end, out int dx, out int dy), "Coherent translation accepted");
    Check((vertical ? dy : dx) == sign * 50, "Use the least moved finger");
}
var basePoints = Origins(3);
Check(!ContinuousGestureTrigger.TryGetTranslation(basePoints,
    new[] { new Point(150, 200), new Point(150, 200), new Point(350, 200) }, out _, out _), "Opposing fingers rejected");
Check(!ContinuousGestureTrigger.TryGetTranslation(basePoints, Origins(2), out _, out _), "Contact count mismatch rejected");

var host = PointCapture.Instance;
var actions = ApplicationManager.Instance.Actions;
for (int n = 2; n <= 5; n++)
foreach (var direction in new[] { Gestures.Left, Gestures.Right, Gestures.Up, Gestures.Down })
    actions.Add(new TestAction(new(n, direction)));
var trigger = new ContinuousGestureTrigger();
var fired = new List<ContinuousGesture>();
trigger.TriggerFired += (_, e) => fired.AddRange(e.FiredActions.Select(a => a.ContinuousGesture));

List<List<Point>> Start(int n)
{
    host.Start();
    var strokes = Origins(n).Select(p => new List<Point> { p }).ToList();
    host.Frame(strokes, strokes.Select(s => s[0]).ToList());
    return strokes;
}
void Move(List<List<Point>> strokes, int x, int y, int? only = null, bool reverseRaw = false, bool fixedRaw = false)
{
    for (int i = 0; i < strokes.Count; i++)
    {
        var p = strokes[i][^1];
        strokes[i].Add(only == null || only == i ? new(p.X + x, p.Y + y) : p);
    }
    var raw = strokes.Select(s => fixedRaw ? s[0] : s[^1]).ToList();
    if (reverseRaw) raw.Reverse();
    Thread.Sleep(4); // The production rate gate intentionally rejects sub-2ms frames.
    host.Frame(strokes, raw);
}
for (int n = 2; n <= 5; n++)
for (int moving = 0; moving < n; moving++)
{
    var strokes = Start(n);
    int before = fired.Count;
    for (int step = 0; step < 3; step++) Move(strokes, 150, 0, moving, reverseRaw: step % 2 == 0);
    Check(fired.Count == before, "Replay: one moving contact must not dispatch continuous actions");
    host.End();
}
foreach (var direction in new[] { Gestures.Left, Gestures.Right, Gestures.Up, Gestures.Down })
{
    var strokes = Start(3);
    int before = fired.Count;
    int x = direction == Gestures.Left ? -50 : direction == Gestures.Right ? 50 : 0;
    int y = direction == Gestures.Up ? -50 : direction == Gestures.Down ? 50 : 0;
    Move(strokes, x, y, reverseRaw: true);
    Check(fired.Count > before && fired.Skip(before).All(g => g.ContactCount == 3 && g.Gesture == direction),
        "All-contact swipe dispatches only the matching direction despite reordered raw packets");
    before = fired.Count;
    Move(strokes, x, y, fixedRaw: true);
    Check(fired.Count > before, "Touchpad fixed visual origins do not stop repeated swipes");
    host.End();
}
{
    var strokes = Start(3);
    Move(strokes, 50, 0);
    host.Cancel();
    int before = fired.Count;
    Move(strokes, 400, 0);
    Check(fired.Count == before, "Cancel discards the old baseline");
    Move(strokes, 50, 0);
    Check(fired.Count > before, "Fresh baseline can fire again");
    host.End();
    before = fired.Count;
    Move(strokes, 400, 0);
    Check(fired.Count == before, "End discards the old baseline");
    // A changed contact set with the same count must not inherit displacement.
    var replaced = Origins(3).Select(p => new List<Point> { new(p.X + 1000, p.Y) }).ToList();
    Thread.Sleep(4);
    host.Frame(replaced, replaced.Select(s => s[0]).ToList());
    Check(fired.Count == before, "Replacement contacts reset the baseline");
}
Console.WriteLine($"PASS {checks} continuous gesture checks: anchored/reordered contacts, real trigger dispatch, all directions, repeat, cancel, end and replacement.");
