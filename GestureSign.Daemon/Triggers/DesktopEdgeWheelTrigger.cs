using System;
using System.Drawing;
using System.Windows.Forms;
using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using ManagedWinapi.Hooks;
namespace GestureSign.Daemon.Triggers;
internal sealed class DesktopEdgeWheelTrigger : Trigger
{
    public DesktopEdgeWheelTrigger() => PointCapture.Instance.MouseHook.MouseWheel += OnWheel;
    private void OnWheel(LowLevelMouseMessage e, ref bool handled)
    {
        if (handled || (e.Flags & 1) != 0 || e.Message != 0x020A || e.MouseData == 0 ||
            PointCapture.Instance.Mode != CaptureMode.Normal || PointCapture.Instance.State != CaptureState.Ready)
            return;
        Point point = e.Point;
        var bounds = Screen.FromPoint(point).Bounds;
        // Only outer desktop edges: an adjacent monitor must remain traversable.
        string edge = null;
        var outside = point;
        if (point.X <= bounds.Left + 1) { edge = "Left"; outside.X = bounds.Left - 1; }
        else if (point.X >= bounds.Right - 2) { edge = "Right"; outside.X = bounds.Right; }
        else if (point.Y <= bounds.Top + 1) { edge = "Top"; outside.Y = bounds.Top - 1; }
        else if (point.Y >= bounds.Bottom - 2) { edge = "Bottom"; outside.Y = bounds.Bottom; }
        if (edge == null || Array.Exists(Screen.AllScreens, screen => screen.Bounds.Contains(outside))) return;
        var name = "DesktopEdge." + edge + (e.MouseData > 0 ? ".WheelUp" : ".WheelDown");
        var actions = ApplicationManager.Instance.PrepareDesktopEdgeWheel(name);
        if (actions.Count == 0) return;
        handled = true;
        OnTriggerFired(new TriggerFiredEventArgs(actions, point) { SourceDevice = Devices.Mouse });
    }
}
