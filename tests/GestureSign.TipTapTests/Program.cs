using GestureSign.Foundation;

var count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); count++; }
TipTapContact A(double x = .5, double y = .5) => new(7, x, y);
TipTapContact B(double x = .7, double y = .5) => new(42, x, y);
var r = new TouchPadTipTapRecognizer();
void Start(bool left = false) { r.Reset(); r.Update([A()], 0); r.Update([A()], 100); var x = r.Update([B(left ? .3 : .7), A()], 150); Check(x.Started == (left ? TouchPadTipTapRecognizer.Left : TouchPadTipTapRecognizer.Right), "candidate direction independent of contact order"); }
Start();
Check(r.Update([A()], 230).Recognized == TouchPadTipTapRecognizer.Right, "right tap fires on tapping finger release");
Check(r.Update([A()], 240).Recognized == null, "no duplicate on held finger frames");
r.Update([A(), B(.3)], 270);
Check(r.Update([A()], 330).Recognized == TouchPadTipTapRecognizer.Left, "repeat left without lifting anchor");
Start(true); Check(r.Update([A()], 220).Recognized == TouchPadTipTapRecognizer.Left, "left tap");
r.Reset(); r.Update([A(), B()], 0); Check(r.Update([A()], 90).Recognized == null, "simultaneous two-finger tap rejected");
r.Reset(); r.Update([A()], 0); r.Update([A(), B()], 30); Check(r.Update([A()], 80).Recognized == null, "near-simultaneous finger arrivals rejected");
Start(); Check(r.Update([B()], 230).Recognized == null, "anchor lifted first rejected");
Start(); Check(r.Update([], 230).Recognized == null, "both fingers lifted together rejected");
Start(); r.Update([A(), B(.8)], 180); Check(r.Update([A()], 230).Recognized == null, "scroll or swipe rejected");
Start(); r.Update([A(.6), B()], 180); Check(r.Update([A(.6)], 230).Recognized == null, "anchor motion rejected");
Start(); Check(r.Update([A()], 500).Recognized == null, "long press rejected");
Start(); Check(r.Update([A()], 155).Recognized == null, "contact glitch rejected");
Start(); r.Update([A(), B(), new(99,.9,.5)], 180); Check(r.Update([A()], 230).Recognized == null, "third finger cancels session");
Start(); r.Update([A(), new(99,.7,.5)], 180); Check(r.Update([A()], 230).Recognized == null, "contact id replacement rejected");
Start(); Check(r.Update([A()], 230, [B(.9)]).Recognized == null, "travel in final release sample rejected");
Start(); r.Reset(); Check(r.Update([A()], 230).Recognized == null, "mode or configuration reset discards candidate");
Start(); Check(r.Update([A()], 2000).Recognized == null, "stale stream discarded");
Start(); Check(r.Update([A()], 20).Recognized == null, "clock rewind discarded");
r.Reset(); r.Update([A()], 0); r.Update([A(.6)], 50); r.Update([A(.6), B(.8)], 200); Check(r.Update([A(.6)], 260).Recognized != null, "anchor may move then settle before tapping");
for (var n = 1; n <= 3; n++)
{
    var held = Enumerable.Range(0, n).Select(i => new TipTapContact(100 + i, .45 + i * .05, .5)).ToArray();
    foreach (var (direction, x, y) in new[] { ("Left", .2, .5), ("Right", .8, .5), ("Up", .5, .2), ("Down", .5, .8) })
    {
        r.Reset(); r.Update(held, 0); r.Update(held, 100);
        var start = r.Update(held.Append(new TipTapContact(99, x, y)).Reverse().ToArray(), 150);
        var expected = TouchPadTipTapRecognizer.GestureName(n, direction);
        Check(start.Started == expected && start.HeldCount == n, $"hold {n} {direction} candidate");
        var done = r.Update(held.Reverse().ToArray(), 230);
        Check(done.Recognized == expected && done.HeldCount == n, $"hold {n} {direction} release");
        r.Update(held.Append(new TipTapContact(98, x, y)).ToArray(), 270);
        Check(r.Update(held, 330).Recognized == expected, $"hold {n} {direction} repeats with new tap id");
    }
}
r.Reset(); r.Update([new(1,.45,.5)], 0); r.Update([new(1,.45,.5), new(2,.5,.5)], 20);
r.Update([new(1,.45,.5), new(2,.5,.5), new(3,.55,.5)], 40);
Check(r.Update([new(1,.45,.5), new(2,.5,.5), new(3,.55,.5), new(9,.5,.2)], 200).Started == "TouchPadTipTap.Hold3.Up", "staggered held group assembles");
Check(r.Update([new(1,.45,.5), new(3,.55,.5), new(9,.5,.2)], 260).Recognized == null, "losing any held finger cancels group");
r.Reset(); r.Update([A()],0);
Check(r.Update([A(),B(.7,.7)],150).Started == null, "ambiguous diagonal does not choose an axis");
r.Reset(); r.Update([new(1,.3,.5),new(2,.7,.5)],0);
Check(r.Update([new(1,.3,.5),new(2,.7,.5),new(3,.5,.5)],150).Started == null, "tap inside held group rejected");
r.Reset(); r.Update([A(),B(),new(3,.6,.5),new(4,.8,.5)],0);
Check(r.Update([A(),B(),new(3,.6,.5)],200).Recognized == null, "four simultaneous contacts are not tiptap");
Console.WriteLine($"PASS {count} checks");
