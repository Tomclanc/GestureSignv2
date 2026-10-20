using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using GestureSign.Common.Input;
using GestureSign.Daemon.Native;

namespace GestureSign.Daemon.Input;

public partial class MessageWindow
{
    private long _rawMessages, _completeFrames, _inputErrors, _invalidDevices;
    private long _lastRawTick;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRegisteredRawInputDevices([Out] RAWINPUTDEVICE[] devices, ref uint count, uint size);

    internal bool ReadInputHealth(out Dictionary<string, object> snapshot)
    {
        var actual = Array.Empty<RAWINPUTDEVICE>();
        bool success = false;
        int error = 0;
        uint count = 0;
        var size = (uint)Marshal.SizeOf<RAWINPUTDEVICE>();
        if (GetRegisteredRawInputDevices(null, ref count, size) != uint.MaxValue && count <= 4096)
        {
            if (count == 0) success = true;
            else
            {
                actual = new RAWINPUTDEVICE[count];
                var returned = GetRegisteredRawInputDevices(actual, ref count, size);
                if (returned != uint.MaxValue) { actual = actual.Take((int)returned).ToArray(); success = true; }
                else error = Marshal.GetLastWin32Error();
            }
        }
        else error = Marshal.GetLastWin32Error();
        var repair = RawInputHealthPolicy.NeedsRepair(success, Handle.ToInt64(), _registeredDeviceList,
            actual.Select(r => new RawInputRegistrationStatus(r.usUsagePage, r.usUsage, r.dwFlags, r.hwndTarget.ToInt64())));
        snapshot = new Dictionary<string, object>
        {
            ["RawMessages"] = _rawMessages, ["FramesDelivered"] = _completeFrames,
            ["InputErrors"] = _inputErrors, ["InvalidDeviceReports"] = _invalidDevices,
            ["LastRawAgeMs"] = _rawMessages == 0 ? -1 : Environment.TickCount64 - _lastRawTick,
            ["RawSource"] = _sourceDevice.ToString(), ["PendingContacts"] = _requiringContactCount,
            ["BufferedContacts"] = _outputTouchs.Count, ["MessageHwnd"] = Handle.ToInt64(),
            ["RegistrationQuerySucceeded"] = success, ["RegistrationQueryError"] = error,
            ["RegistrationMismatch"] = repair, ["ExpectedUsages"] = _registeredDeviceList.ToArray(),
            ["Registrations"] = actual.Select(r => new RawInputRegistrationStatus(r.usUsagePage, r.usUsage, r.dwFlags, r.hwndTarget.ToInt64())).ToArray()
        };
        return repair;
    }
}
