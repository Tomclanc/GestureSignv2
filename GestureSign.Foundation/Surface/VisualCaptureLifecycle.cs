using System.Threading;

namespace GestureSign.Foundation.Surface;

public sealed class VisualCaptureLifecycle
{
    private int _nextGeneration, _activeGeneration, _displayedGeneration;
    public int ActiveGeneration => Volatile.Read(ref _activeGeneration);
    public int DisplayedGeneration => Volatile.Read(ref _displayedGeneration);
    public int Start() { var generation = Interlocked.Increment(ref _nextGeneration); Volatile.Write(ref _activeGeneration, generation); return generation; }
    public bool IsActive(int generation) => generation > 0 && ActiveGeneration == generation;
    public bool TryMarkDisplayed(int generation) { if (!IsActive(generation)) return false; Volatile.Write(ref _displayedGeneration, generation); return true; }
    public void Invalidate(int generation) { if (generation > 0) Interlocked.CompareExchange(ref _activeGeneration, 0, generation); }
    public bool ShouldHideForEndedGeneration(int endedGeneration) { var active = ActiveGeneration; return active == 0 || active == endedGeneration || DisplayedGeneration != active; }
    public void MarkHidden() => Volatile.Write(ref _displayedGeneration, 0);
}
