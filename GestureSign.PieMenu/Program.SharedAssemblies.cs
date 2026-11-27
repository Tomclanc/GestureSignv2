#if SHARE_PIE_HOST_ASSEMBLIES
using GestureSign.Shared;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using System.Runtime.CompilerServices;

namespace GestureSign.PieMenu;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        HostAssemblySharing.RegisterIfPresent(args, "Microsoft.Windows.SDK.NET", "Microsoft.WinUI");
        StartApp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void StartApp()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
#endif
