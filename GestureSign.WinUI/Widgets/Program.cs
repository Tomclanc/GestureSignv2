using System;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace GestureSign.WinUI;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Array.Exists(args, arg => string.Equals(arg, "-Embedding", StringComparison.OrdinalIgnoreCase)))
        {
            var provider = new Thread(WidgetServer.Run);
            provider.SetApartmentState(ApartmentState.MTA);
            provider.Start();
            provider.Join();
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
