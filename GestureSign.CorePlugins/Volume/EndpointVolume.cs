using System;
using System.Runtime.InteropServices;

namespace GestureSign.CorePlugins.Volume;

internal static class EndpointVolume
{
    internal static float Next(float current, int points)
        => Math.Clamp((float)Math.Round(current * 100, MidpointRounding.AwayFromZero) + points, 0, 100) / 100f;

    private static readonly object Gate = new();

    internal static bool Adjust(int points)
    {
        lock (Gate)
        {
            float? target = null;
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    if (AdjustOnce(points, ref target, attempt)) return true;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Volume failure. Attempt={attempt}, HRESULT=0x{ex.HResult:X8}, Error={ex.Message}");
                }
            }
            return false;
        }
    }

    private static bool AdjustOnce(int points, ref float? target, int attempt)
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
            device.GetId(out var deviceId);
            api.GetMute(out var muted);
            // Retain the absolute target across retries. A successful write
            // followed by a failed read must never apply the step twice.
            target ??= Next(current, Math.Clamp(points, -100, 100));
            Console.WriteLine($"Volume request. Backend=CoreAudio, Attempt={attempt}, Device={deviceId}, Step={points}, Before={current * 100:F2}, Target={target.Value * 100:F2}, Muted={muted}");
            var context = Guid.Empty;
            api.SetMasterVolumeLevelScalar(target.Value, ref context);
            api.GetMasterVolumeLevelScalar(out var after);
            bool success = Math.Abs(after - target.Value) <= .005f;
            Console.WriteLine($"Volume result. Success={success}, Device={deviceId}, After={after * 100:F2}, Target={target.Value * 100:F2}");
            return success;
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
        void OpenPropertyStore(int mode, out IntPtr properties);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
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
        void SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
