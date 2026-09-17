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
