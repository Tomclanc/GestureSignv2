using System.Collections.Generic;
using System.Linq;

namespace GestureSign.WinUI;

// Each pointer owns one stroke. Finger count comes from real captured strokes,
// never from duplicating a single stroke or sorting pointer identifiers.
internal sealed class GestureDrawingSession(List<List<(double X, double Y)>> strokes)
{
    private readonly Dictionary<uint, List<(double X, double Y)>> _active = new();
    public int ActiveCount => _active.Count;

    public List<(double X, double Y)> Begin(uint pointerId, double x, double y)
    {
        if (_active.TryGetValue(pointerId, out var existing)) return existing;
        if (_active.Count == 0) strokes.Clear();
        var stroke = new List<(double X, double Y)> { (x, y) };
        _active.Add(pointerId, stroke);
        strokes.Add(stroke);
        return stroke;
    }

    public bool Move(uint pointerId, double x, double y, bool finalPoint = false)
    {
        if (!_active.TryGetValue(pointerId, out var stroke)) return false;
        var last = stroke[^1];
        var distance = System.Math.Abs(last.X - x) + System.Math.Abs(last.Y - y);
        if (distance == 0 || !finalPoint && distance < 4) return false;
        stroke.Add((x, y));
        return true;
    }

    public void End(uint pointerId) => _active.Remove(pointerId);
    public void Clear() { _active.Clear(); strokes.Clear(); }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> Snapshot()
        => strokes.Where(stroke => stroke.Count > 0)
            .Select(stroke => (IReadOnlyList<(double X, double Y)>)stroke.ToArray()).ToArray();
}