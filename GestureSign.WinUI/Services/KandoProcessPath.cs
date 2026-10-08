using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GestureSign.WinUI.Services;

internal static class KandoProcessPath
{
    // MainModule requests VM_READ; identification only needs limited query access.
    public static string? Read(Process process, Action<string>? log = null)
    {
        using var handle = OpenProcess(0x1000, false, process.Id);
        if (handle.IsInvalid)
        {
            log?.Invoke($"Kando path query skipped PID={process.Id}, error={Marshal.GetLastWin32Error()}");
            return null;
        }
        var path = new StringBuilder(32768);
        var length = path.Capacity;
        if (QueryFullProcessImageName(handle, 0, path, ref length)) return path.ToString();
        log?.Invoke($"Kando path query skipped PID={process.Id}, error={Marshal.GetLastWin32Error()}");
        return null;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(SafeProcessHandle process, uint flags, StringBuilder path, ref int length);
}
