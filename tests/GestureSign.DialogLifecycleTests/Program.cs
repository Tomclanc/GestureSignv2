using Microsoft.UI.Xaml.Controls;

var window = new GestureSign.WinUI.MainWindow();
var first = new ContentDialog();
var second = new ContentDialog();
var a = window.Show(first);
var b = window.Show(second);
Check(first.Started && !second.Started, "Only one dialog starts");
first.CompleteResult();
Check(!a.IsCompleted && !second.Started, "ShowAsync result alone does not release the next editor");
first.CompleteClose();
await a.WaitAsync(TimeSpan.FromSeconds(2));
await Task.Yield();
while (!second.Started) await Task.Delay(1);
Check(second.Started, "Queued dialog starts after Closed");
second.CompleteResult(); second.CompleteClose(); await b;
Check(ContentDialog.Active == 0, "All dialogs release their native slot");
try { await window.Show(new ContentDialog { Fail = true }); throw new Exception("Expected failure"); }
catch (InvalidOperationException) { }
var recovery = new ContentDialog(); var c = window.Show(recovery);
Check(recovery.Started, "Failed opening does not lock future dialogs");
recovery.CompleteResult(); recovery.CompleteClose(); await c;
for (var i = 0; i < 20; i++)
{
    var dialog = new ContentDialog(); var task = window.Show(dialog);
    dialog.CompleteResult(); dialog.CompleteClose(); await task;
}
Check(ContentDialog.Active == 0, "Repeated command editor transitions leave no stale dialog");
Console.WriteLine("6 dialog lifecycle regression checks passed.");
void Check(bool condition, string name) { if (!condition) throw new Exception(name); }

namespace GestureSign.WinUI
{
    public sealed partial class MainWindow
    {
        private readonly RootStub Root = new();
        private readonly Microsoft.UI.Dispatching.DispatcherQueue DispatcherQueue = new();
        private readonly StoreStub _legacyData = new();
        private void LogException(Exception error) { }
        public Task<ContentDialogResult> Show(ContentDialog dialog) => ShowDialogAsync(dialog);
        private sealed class RootStub { public object XamlRoot { get; } = new(); }
        private sealed class StoreStub { public string LocalPath => Path.Combine(Path.GetTempPath(), "GestureSign-dialog-regression"); }
    }
}
namespace Microsoft.UI.Dispatching
{
    public enum DispatcherQueuePriority { Low }
    public sealed class DispatcherQueue
    {
        public bool TryEnqueue(DispatcherQueuePriority priority, Action action) { Task.Run(action); return true; }
    }
}
namespace Microsoft.UI.Xaml.Controls
{
    public enum ContentDialogResult { None }
    public sealed class ContentDialogClosedEventArgs : EventArgs { }
    public sealed class ContentDialog
    {
        public static int Active;
        public object? XamlRoot { get; set; }
        public string Title { get; set; } = "Test";
        public event Action<ContentDialog, ContentDialogClosedEventArgs>? Closed;
        public bool Started { get; private set; }
        public bool Fail { get; set; }
        private readonly TaskCompletionSource<ContentDialogResult> result = new();
        public Task<ContentDialogResult> ShowAsync()
        {
            if (Fail || Active != 0) throw new InvalidOperationException("Only a single ContentDialog can be open");
            Active++; Started = true; return result.Task;
        }
        public void CompleteResult() => result.SetResult(ContentDialogResult.None);
        public void CompleteClose() { Active--; Closed?.Invoke(this, new()); }
    }
}
