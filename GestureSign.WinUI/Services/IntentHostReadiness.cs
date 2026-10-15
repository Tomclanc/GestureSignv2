using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace GestureSign.WinUI.Services;

internal static class IntentHostReadiness
{
    // Retry only before connecting. Never replay a command whose response was lost.
    internal static async Task<T> WaitAsync<T>(Func<Task<T>> connect, Func<Exception, bool> unavailable,
        Func<bool> running, Action start, TimeSpan budget)
    {
        var elapsed = Stopwatch.StartNew();
        bool started = false;
        while (true)
        {
            try { return await connect(); }
            catch (Exception ex) when (unavailable(ex) && elapsed.Elapsed < budget)
            {
                if (!started && !running()) { start(); started = true; }
                await Task.Delay(100);
            }
        }
    }
}
