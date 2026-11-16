using System.Drawing;
using GestureSign.Common.Input;

// Only the device/event source, action lookup and DPI are substituted. Tests
// compile the production trigger and dispatch its real capture events.
namespace GestureSign.Common.Applications
{
    public interface IAction { ContinuousGesture ContinuousGesture { get; } }
    public sealed record TestAction(ContinuousGesture ContinuousGesture) : IAction;
    public sealed class ApplicationManager
    {
        public static ApplicationManager Instance { get; } = new();
        public List<IAction> Actions { get; } = new();
        public List<IAction> GetRecognizedDefinedAction(Func<IAction, bool> predicate) => Actions.Where(predicate).ToList();
    }
}
namespace GestureSign.Common.Log
{
    public static class Logging { public static void LogMessage(string text) => Console.WriteLine(text); }
}
namespace GestureSign.Daemon.Native
{
    public static class DpiHelper { public static int GetSystemDpi() => 96; }
}
namespace GestureSign.Daemon.Input
{
    public sealed class PointCapture
    {
        public static PointCapture Instance { get; } = new();
        public CaptureState State { get; set; } = CaptureState.Capturing;
        public event PointsCapturedEventHandler PointCaptured, CaptureStarted, CaptureCanceled;
        public event EventHandler CaptureEnded;
        public void Start() => CaptureStarted?.Invoke(this, new(new List<Point>()));
        public void Frame(List<List<Point>> strokes, List<Point> raw) => PointCaptured?.Invoke(this, new(strokes, raw));
        public void Cancel() => CaptureCanceled?.Invoke(this, new(new List<Point>()));
        public void End() => CaptureEnded?.Invoke(this, EventArgs.Empty);
    }
}
