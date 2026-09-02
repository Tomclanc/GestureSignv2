using System.Threading;

namespace GestureSign.Foundation.Input;

public enum LivePreviewTransition { None, Show, Clear }

public sealed class CaptureSession
{
    private static long _nextId;
    public long Id { get; } = Interlocked.Increment(ref _nextId);
    public CaptureSessionState State { get; private set; }
    public int RequiredContactCount { get; private set; } = 1;
    public int VisualGeneration { get; private set; }
    public string? VisibleActionName { get; private set; }
    public string? FallbackGestureName { get; private set; }
    public string? FallbackActionName { get; private set; }
    public int FallbackPointCount { get; private set; }
    private bool IsInputActive => State is CaptureSessionState.Capturing or CaptureSessionState.Previewing;

    public bool Accept(int requiredContactCount)
    {
        if (State != CaptureSessionState.Pending) return false;
        RequiredContactCount = Math.Max(1, requiredContactCount);
        State = CaptureSessionState.Capturing;
        return true;
    }

    public bool AttachVisualGeneration(int generation)
    {
        if (!IsInputActive || generation <= 0) return false;
        VisualGeneration = generation;
        return true;
    }

    public LivePreviewTransition UpdatePreview(string gestureName, string actionName, int pointCount)
    {
        if (!IsInputActive || string.IsNullOrWhiteSpace(gestureName) || string.IsNullOrWhiteSpace(actionName)) return LivePreviewTransition.None;
        FallbackGestureName = gestureName;
        FallbackActionName = actionName;
        FallbackPointCount = Math.Max(0, pointCount);
        if (string.Equals(VisibleActionName, actionName, StringComparison.Ordinal)) return LivePreviewTransition.None;
        VisibleActionName = actionName;
        State = CaptureSessionState.Previewing;
        return LivePreviewTransition.Show;
    }

    public LivePreviewTransition InvalidatePreview()
    {
        if (!IsInputActive) return LivePreviewTransition.None;
        var hadPreview = VisibleActionName is not null || FallbackGestureName is not null;
        VisibleActionName = FallbackGestureName = FallbackActionName = null;
        FallbackPointCount = 0;
        State = CaptureSessionState.Capturing;
        return hadPreview ? LivePreviewTransition.Clear : LivePreviewTransition.None;
    }

    public int BeginRecognition()
    {
        if (!IsInputActive) return 0;
        VisibleActionName = null;
        State = CaptureSessionState.Recognizing;
        return DetachVisualGeneration();
    }

    public bool BeginExecution()
    {
        if (State != CaptureSessionState.Recognizing) return false;
        State = CaptureSessionState.Executing;
        return true;
    }

    public void Complete()
    {
        if (State is CaptureSessionState.Completed or CaptureSessionState.Canceled) return;
        VisibleActionName = null;
        State = CaptureSessionState.Completed;
        VisualGeneration = 0;
    }

    public int Cancel()
    {
        if (State is CaptureSessionState.Completed or CaptureSessionState.Canceled) return 0;
        VisibleActionName = FallbackGestureName = FallbackActionName = null;
        FallbackPointCount = 0;
        State = CaptureSessionState.Canceled;
        return DetachVisualGeneration();
    }

    public bool IsVisualFrameCurrent(int generation) => IsInputActive && generation > 0 && VisualGeneration == generation;
    private int DetachVisualGeneration() { var generation = VisualGeneration; VisualGeneration = 0; return generation; }
}
