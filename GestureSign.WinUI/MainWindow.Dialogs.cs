using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);

    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        await _dialogGate.WaitAsync();
        var closed = new TaskCompletionSource<bool>();
        void OnClosed(ContentDialog sender, ContentDialogClosedEventArgs args) => closed.TrySetResult(true);
        dialog.Closed += OnClosed;
        try
        {
            dialog.XamlRoot = Root.XamlRoot;
            LogDialog($"Opening: {dialog.Title}");
            var result = await dialog.ShowAsync();
            // Hide/ShowAsync completion and the closing animation can finish at
            // different times. Do not start the next editor until Closed has run
            // and WinUI has completed the current dispatcher turn.
            await closed.Task;
            var idle = new TaskCompletionSource<bool>();
            if (DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () => idle.TrySetResult(true)))
                await idle.Task;
            LogDialog($"Closed: {dialog.Title}; result={result}");
            return result;
        }
        catch (Exception error)
        {
            LogException(error);
            throw;
        }
        finally
        {
            dialog.Closed -= OnClosed;
            _dialogGate.Release();
        }
    }

    private void LogDialog(string message)
    {
        try
        {
            System.IO.Directory.CreateDirectory(_legacyData.LocalPath);
            System.IO.File.AppendAllText(System.IO.Path.Combine(_legacyData.LocalPath, "GestureSign.WinUI.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Dialog {message}\r\n");
        }
        catch { }
    }
}
