using System.Drawing;
namespace GestureSign.Common.Input { public enum Devices { Mouse } public enum CaptureMode { Normal, Training, UserDisabled } public enum CaptureState { Ready, Capturing } }
namespace ManagedWinapi.Hooks {
 public class LowLevelMouseMessage { public Point Point; public int Flags, Message, MouseData; }
 public delegate void WheelHandler(LowLevelMouseMessage e, ref bool handled);
 public class MouseHook { public event WheelHandler MouseWheel; public bool Send(LowLevelMouseMessage e, bool handled) { MouseWheel?.Invoke(e, ref handled); return handled; } }
}
namespace GestureSign.Daemon.Input { public class PointCapture { public static PointCapture Instance = new(); public ManagedWinapi.Hooks.MouseHook MouseHook = new(); public GestureSign.Common.Input.CaptureMode Mode; public GestureSign.Common.Input.CaptureState State; } }
namespace GestureSign.Common.Applications { public class ApplicationManager { public static ApplicationManager Instance = new(); public string Last; public bool Allow = true; public List<object> PrepareDesktopEdgeWheel(string name) { Last = name; return Allow ? [new()] : []; } } }
namespace GestureSign.Daemon.Triggers {
 public class Trigger { public event EventHandler<TriggerFiredEventArgs> TriggerFired; protected void OnTriggerFired(TriggerFiredEventArgs e) => TriggerFired?.Invoke(this,e); }
 public class TriggerFiredEventArgs : EventArgs { public TriggerFiredEventArgs(List<object> actions, Point point) {} public GestureSign.Common.Input.Devices? SourceDevice; }
}
namespace System.Windows.Forms { public class Screen { public Rectangle Bounds; public Screen(Rectangle bounds) => Bounds = bounds; public static Screen[] AllScreens; public static Screen FromPoint(Point p) => AllScreens.First(s => s.Bounds.Contains(p)); } }
namespace WindowsInput { public class InputSimulator { public static int Down, Up; public static bool FailUp; public InputSimulator Mouse => this; public void LeftButtonDown() => Interlocked.Increment(ref Down); public void LeftButtonUp() { if(FailUp) throw new Exception("simulated failure"); Interlocked.Increment(ref Up); } } }
namespace GestureSign.Common.Log { public static class Logging { public static void LogException(Exception e) {} } }
