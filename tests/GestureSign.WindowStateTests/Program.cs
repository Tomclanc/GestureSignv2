using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using GestureSign.Common.Plugins;
using GestureSign.CorePlugins;
using ManagedWinapi.Windows;

class Program
{
    [STAThread]
    static void Main()
    {
        var toggle = new MaximizeRestore(); var minimize = new Minimize();
        Check(!toggle.Gestured(null) && !minimize.Gestured(null), "Null target rejected");
        Check(!toggle.Gestured(Point(SystemWindow.ShellWindow)), "Desktop rejected");
        using var form = new TestForm { Text = "GestureSign window state regression", Size = new Size(350, 160), ShowInTaskbar = false };
        using var child = new TextBox(); form.Controls.Add(child);
        form.Show(); Application.DoEvents();
        var root = new SystemWindow(form.Handle);
        var captured = Point(new SystemWindow(child.Handle));
        Request(() => toggle.Gestured(captured)); Wait(root, FormWindowState.Maximized);
        Request(() => toggle.Gestured(captured)); Wait(root, FormWindowState.Normal);
        Request(() => minimize.Gestured(captured)); Wait(root, FormWindowState.Minimized);
        Request(() => toggle.Gestured(captured)); Wait(root, FormWindowState.Maximized);
        Request(() => toggle.Gestured(captured)); Wait(root, FormWindowState.Normal);
        var stale = Point(root); form.Dispose();
        Check(!toggle.Gestured(stale) && !minimize.Gestured(stale), "Destroyed target rejected");
        Console.WriteLine("8 window state checks passed (worker thread, child HWND, maximize, restore, minimize, minimized-to-maximized, null/desktop/stale targets).");
    }
    static PointInfo Point(SystemWindow window) => new(new List<Point>(), new List<List<Point>>(), window, new SynchronizationContext());
    static void Request(Func<bool> operation)
    {
        var task = Task.Run(operation);
        var watch = Stopwatch.StartNew();
        // Deliberately do not pump the owning UI thread until the async request
        // returns; synchronous ShowWindow from a worker can deadlock here.
        Check(task.Wait(TimeSpan.FromSeconds(2)), "Cross-thread request blocked");
        Check(task.Result, "Window rejected request");
    }
    static void Wait(SystemWindow window, FormWindowState state)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < 2000)
        {
            Application.DoEvents();
            if (window.WindowState == state) return;
            Thread.Sleep(10);
        }
        throw new Exception($"Expected {state}, actual {window.WindowState}");
    }
    static void Check(bool result, string message) { if (!result) throw new Exception(message); }
    sealed class TestForm : Form { protected override bool ShowWithoutActivation => true; }
}
