using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace GestureSign.WinUI;

internal static class WidgetServer
{
    private static readonly Guid ClassId = new("9a420e4f-1331-435f-8ee9-480c248569f8");

    internal static void Run()
    {
        // The provider does not run a UI message pump. Register its factory in the MTA.
        Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 0));
        try
        {
            ComWrappersSupport.InitializeComWrappers();
            using var changed = new AutoResetEvent(false);
            var provider = new QuickActionsWidget(changed);
            var factory = new WidgetClassFactory(provider);
            var wrappers = new StrategyBasedComWrappers();
            var factoryPointer = wrappers.GetOrCreateComInterfaceForObject(factory, CreateComInterfaceFlags.None);
            uint cookie;
            try { Marshal.ThrowExceptionForHR(CoRegisterClassObject(in ClassId, factoryPointer, 4, 1, out cookie)); }
            finally { Marshal.Release(factoryPointer); }
            try
            {
                // End the server after the last pin is removed. Leave time for COM to deliver
                // the first callback and for the user to pin the card again.
                while (changed.WaitOne(TimeSpan.FromSeconds(30)) || provider.HasPins) { }
            }
            finally
            {
                CoRevokeClassObject(cookie);
                GC.KeepAlive(factory);
            }
        }
        finally { CoUninitialize(); }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRegisterClassObject(in Guid clsid, IntPtr factory,
        uint context, uint flags, out uint cookie);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoRevokeClassObject(uint cookie);
}

[GeneratedComInterface]
[Guid("00000001-0000-0000-C000-000000000046")]
internal partial interface IWidgetClassFactory
{
    [PreserveSig] int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result);
    [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool locked);
}

[GeneratedComClass]
internal sealed partial class WidgetClassFactory : IWidgetClassFactory
{
    private readonly QuickActionsWidget _provider;

    internal WidgetClassFactory(QuickActionsWidget provider) => _provider = provider;

    public int CreateInstance(IntPtr outer, in Guid iid, out IntPtr result)
    {
        result = IntPtr.Zero;
        if (outer != IntPtr.Zero) return unchecked((int)0x80040110); // CLASS_E_NOAGGREGATION
        IntPtr instance = IntPtr.Zero;
        try
        {
            instance = MarshalInspectable<IWidgetProvider>.FromManaged(_provider);
            return Marshal.QueryInterface(instance, in iid, out result);
        }
        catch (Exception exception) { return Marshal.GetHRForException(exception); }
        finally { if (instance != IntPtr.Zero) Marshal.Release(instance); }
    }

    public int LockServer(bool locked) => 0;
}
