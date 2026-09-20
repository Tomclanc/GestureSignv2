using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GestureSign.Common.Plugins;
using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using ManagedWinapi.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        Assert(!Activate(null), "null target rejected");
        Assert(!Activate(new SystemWindow(IntPtr.Zero)), "zero target rejected");
        using (var disposed = new Form())
        {
            var staleHandle = disposed.Handle;
            disposed.Dispose();
            Assert(!Activate(new SystemWindow(staleHandle)), "destroyed target rejected");
        }

        using (var background = new Form())
        {
            var appWindow = new SystemWindow(background.Handle);
            var desktop = SystemWindow.ShellWindow;
            Assert(desktop != null && desktop.HWnd != IntPtr.Zero, "desktop shell exists");
            var select = typeof(ApplicationManager).GetMethod("SelectTouchPadTarget",
                BindingFlags.Static | BindingFlags.NonPublic);
            var selectedDesktop = (SystemWindow)select.Invoke(null, new object[] { desktop, appWindow });
            Assert(selectedDesktop.HWnd == desktop.HWnd,
                "desktop hit stays desktop while another application is foreground");
            var fullscreen = typeof(ApplicationManager).GetMethod("IsFullScreenWindow",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert(!(bool)fullscreen.Invoke(ApplicationManager.Instance, new object[] { selectedDesktop }),
                "desktop is not rejected by fullscreen exclusion");
            Assert(((SystemWindow)select.Invoke(null, new object[] { appWindow, desktop })).HWnd == appWindow.HWnd,
                "application under cursor remains target when desktop is foreground");
            Assert(((SystemWindow)select.Invoke(null, new object[] { null, appWindow })).HWnd == appWindow.HWnd,
                "missing pointer target falls back to foreground");
        }

        TestTipTapActions();

        if (Array.IndexOf(args, "--interactive") < 0)
        {
            Console.WriteLine("Use --interactive on a Windows desktop to test focus and click preservation.");
            return;
        }

        var previousForeground = SystemWindow.ForegroundWindow;
        var previousCursor = Cursor.Position;
        try
        {
            using var target = new Form { Text = "GestureSign activation regression", Size = new Size(420, 180) };
            using var other = new Form { Text = "GestureSign other window", Size = new Size(300, 160),
                StartPosition = FormStartPosition.Manual, Location = new Point(600, 300) };
            var text = new TextBox { Text = "Preserve this selection", Location = new Point(20, 20), Width = 300 };
            var button = new Button { Text = "Must not click", Location = new Point(20, 60), Width = 180 };
            int clicks = 0;
            button.Click += (_, _) => clicks++;
            target.Controls.Add(text);
            target.Controls.Add(button);
            target.Show();
            Assert(ActivateFromWorker(new SystemWindow(target.Handle)), "activate test window");
            text.Focus();
            text.Select(2, 8);
            Application.DoEvents();
            Cursor.Position = button.PointToScreen(new Point(20, 10));
            Assert(SystemWindow.ForegroundWindow.HWnd == target.Handle, "test target in foreground");
            var child = new SystemWindow(text.Handle);
            Assert(ActivateFromWorker(child), "already foreground child activates its root");
            Assert(text.Focused && text.SelectionStart == 2 && text.SelectionLength == 8 && clicks == 0,
                "foreground activation preserves control, selection and click count");

            other.Show();
            Assert(ActivateFromWorker(new SystemWindow(other.Handle)), "activate other window");
            Application.DoEvents();
            Assert(SystemWindow.ForegroundWindow.HWnd == other.Handle, "other window in foreground");
            var capture = DispatchProxy.Create<IPointCapture, TouchPadCaptureProxy>();
            var selected = (SystemWindow)typeof(ApplicationManager)
                .GetMethod("ResolveCaptureWindow", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(ApplicationManager.Instance, new object[] { capture, Cursor.Position });
            Assert(selected.HWnd == target.Handle, "touchpad selects inactive window under pointer instead of foreground");
            Assert(ActivateFromWorker(selected), "captured background target activates before action");
            Application.DoEvents();
            Assert(SystemWindow.ForegroundWindow.HWnd == target.Handle && text.Focused &&
                text.SelectionStart == 2 && text.SelectionLength == 8 && clicks == 0,
                "background activation preserves control, selection and click count");
        }
        finally
        {
            Cursor.Position = previousCursor;
            SystemWindow.ActivateWindow(previousForeground);
        }
    }

    private static void TestTipTapActions()
    {
        var manager = ApplicationManager.Instance;
        manager.LoadingTask.GetAwaiter().GetResult();
        var field = typeof(ApplicationManager).GetField("_applications", BindingFlags.Instance | BindingFlags.NonPublic);
        var original = field.GetValue(manager);
        try
        {
            var action = new GestureSign.Common.Applications.Action
            {
                GestureName = "TouchPadTipTap.Left", Name = "TipTap test",
                Commands = new[] { new Command { IsEnabled = true, PluginClass = "test" } }
            };
            var global = new GlobalApp { Actions = new[] { action }, LimitNumberOfFingers = 2 };
            var apps = new System.Collections.Generic.List<IApplication> { global };
            field.SetValue(manager, apps);
            var desktop = SystemWindow.ShellWindow;
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName).Count == 1,
                "configured TipTap resolves on desktop");
            Assert(manager.CaptureWindow.HWnd == desktop.HWnd, "TipTap keeps captured target");
            Assert(manager.PrepareTouchPadTipTap(desktop, "TouchPadTipTap.Right").Count == 0,
                "unassigned TipTap has no action");
            action.IsEnabled = false;
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName).Count == 0, "disabled TipTap stays inactive");
            action.IsEnabled = true;
            action.IgnoredDevices = Devices.TouchPad;
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName).Count == 0, "TipTap respects excluded device");
            action.IgnoredDevices = Devices.None;
            global.LimitNumberOfFingers = 3;
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName).Count == 0, "TipTap respects minimum finger count");
            action.GestureName = "TouchPadTipTap.Hold3.Down";
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName, 4).Count == 1,
                "four-contact TipTap satisfies three-finger minimum");
            global.LimitNumberOfFingers = 5;
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName, 4).Count == 0,
                "four-contact TipTap respects higher minimum");
            global.LimitNumberOfFingers = 2;
            apps.Add(new IgnoredApp("desktop exclusion", MatchUsing.WindowClass, desktop.ClassName, false, true));
            Assert(manager.PrepareTouchPadTipTap(desktop, action.GestureName).Count == 0, "TipTap respects ignored application");
        }
        finally { field.SetValue(manager, original); }
    }

    private static bool Activate(SystemWindow window) => (bool)typeof(PluginManager)
        .GetMethod("ActivateTargetWindow", BindingFlags.Static | BindingFlags.NonPublic)
        .Invoke(null, new object[] { window });

    private static bool ActivateFromWorker(SystemWindow window)
    {
        var task = Task.Run(() => Activate(window));
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted && timer.ElapsedMilliseconds < 5000)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        Assert(task.IsCompleted, "activation completes within five seconds");
        return task.GetAwaiter().GetResult();
    }

    private static void Assert(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException(name);
        Console.WriteLine("PASS " + name);
    }
}

public class TouchPadCaptureProxy : DispatchProxy
{
    protected override object Invoke(MethodInfo method, object[] args)
    {
        if (method.Name == "get_SourceDevice") return Devices.TouchPad;
        throw new NotSupportedException(method.Name);
    }
}
