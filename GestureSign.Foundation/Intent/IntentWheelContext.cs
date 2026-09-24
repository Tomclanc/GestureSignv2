namespace GestureSign.Foundation.Intent;

// Wheel input is evidence of intent, not proof that document contents moved.
public sealed class IntentWheelContext
{
    private readonly object _sync = new();
    private readonly Queue<(double Time, int Delta)> _events = new();
    private long _window;
    public void Reset() { lock (_sync) { _events.Clear(); _window = 0; } }
    public void Record(long window, double time, int delta)
    {
        lock (_sync)
        {
            if (window == 0) { _events.Clear(); _window = 0; return; }
            if (_window != window) _events.Clear();
            _window = window;
            while (_events.Count > 0 && time - _events.Peek().Time > 900) _events.Dequeue();
            if (delta == 0) return;
            if (_events.Count >= 64) _events.Dequeue();
            _events.Enqueue((time, Math.Clamp(delta, -120, 120)));
        }
    }
    public bool WasScrolling(long window, double before)
    {
        lock (_sync)
        {
            if (window == 0 || _window != window) return false;
            var recent = _events.Where(e => e.Time < before && before - e.Time <= 900).ToArray();
            return recent.Length >= 3 && before - recent[^1].Time <= 500 &&
                recent[^1].Time - recent[0].Time >= 80 &&
                recent.Sum(e => Math.Abs(e.Delta)) >= 240;
        }
    }
}
