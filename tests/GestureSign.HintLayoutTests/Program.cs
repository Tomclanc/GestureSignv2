using System.Drawing;
using GestureSign.Daemon.Surface;



var cases = new[] {
    (new Rectangle(0, 0, 1920, 1040), new Point(1, 1)),
    (new Rectangle(1920, 0, 1920, 1040), new Point(1, 1)),
    (new Rectangle(-2560, 0, 2560, 1400), new Point(-2559, 1)),
    (new Rectangle(0, -1440, 2560, 1400), new Point(1, -1439)),
    (new Rectangle(40, 0, 1880, 1080), new Point(1, 1))
};
foreach (var (work, origin) in cases)
foreach (var width in new[] { 240f, 9000f }) {
    var rect = GestureHintLayout.Place(work, origin, new SizeF(width, 60));
    rect.Offset(origin.X, origin.Y);
    if (rect.Left < work.Left || rect.Right > work.Right || rect.Bottom > work.Bottom || rect.Top < work.Top)
        throw new Exception($"Hint crosses monitor boundary: {work}, {rect}");
    if (Math.Abs((rect.Left + rect.Right) / 2 - (work.Left + work.Right) / 2f) > .01f)
        throw new Exception("Hint is not centered within the selected monitor");
}
Console.WriteLine("PASS: 10 monitor layout cases, including horizontal, negative origins, stacked monitors, taskbar and long names.");

