using System;
using System.Runtime.InteropServices;

namespace GestureSign.CorePlugins.Volume;

internal static class EndpointVolume
{
    internal static float Next(float current, int points)
        => Math.Clamp((float)Math.Round(current * 100, MidpointRounding.AwayFromZero) + points, 0, 100) / 100f;

    internal static void Adjust(int points)
    {
        object enumerator = null, endpoint = null, volume = null;
        try
        {
            enumerator = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
            ((IDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out var device);
            endpoint = device;
            var iid = typeof(IEndpointVolume).GUID;
            device.Activate(ref iid, 23, IntPtr.Zero, out volume);
            var api = (IEndpointVolume)volume;
            api.GetMasterVolumeLevelScalar(out var current);
            var context = Guid.Empty;
            api.SetMasterVolumeLevelScalar(Next(current, Math.Clamp(points, -100, 100)), ref context);
        }
        finally
        {
            if (volume != null) Marshal.ReleaseComObject(volume);
            if (endpoint != null) Marshal.ReleaseComObject(endpoint);
            if (enumerator != null) Marshal.ReleaseComObject(enumerator);
        }
    }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, int mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice
    {
        void Activate(ref Guid iid, int context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object value);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr callback);
        void UnregisterControlChangeNotify(IntPtr callback);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level);
    }
}
