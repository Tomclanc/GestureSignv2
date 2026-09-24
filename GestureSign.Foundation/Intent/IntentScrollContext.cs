namespace GestureSign.Foundation.Intent;

// Short-lived evidence only; never turns a prediction into a training label.
public sealed class IntentScrollContext
{
    private long _window;
    private double _end = -10000;
    private int _count;
    private int _axis = -1;
    public void Reset() { _count = 0; _axis = -1; _end = -10000; }
    public bool Add(IntentSample sample, long window, double started, double ended, bool recentWheel = false)
    {
        if (window == 0 || window != _window || started - _end > 650 || started < _end) Reset();
        _window = window;
        var f = IntentFeatures.Extract(sample);
        // Three quick, nearly straight two-finger strokes establish a scroll burst.
        int axis = f[4] > .88f ? 0 : f[5] > .88f ? 1 : -1;
        bool straight = axis >= 0 && f[2] > .94f && ended - started <= 900;
        double width = sample.Frames.Max(v => v.Points.Average(p => p.X)) - sample.Frames.Min(v => v.Points.Average(p => p.X));
        double height = sample.Frames.Max(v => v.Points.Average(p => p.Y)) - sample.Frames.Min(v => v.Points.Average(p => p.Y));
        bool sameAxis = _axis == 0 ? width > height : height > width;
        // Only a predominantly straight continuation with a small hook is suspicious;
        // a deliberate L with substantial movement on both axes is left to the model.
        bool suspicious = ((_count >= 3 && sameAxis) || (recentWheel && height > width)) && f[1] < .4f && f[2] > .75f && ended - started <= 900;
        if (straight) { _count = axis == _axis ? _count + 1 : 1; _axis = axis; }
        else { _count = 0; _axis = -1; }
        _end = ended;
        return suspicious;
    }
}
