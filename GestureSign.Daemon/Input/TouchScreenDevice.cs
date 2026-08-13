using GestureSign.Common.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace GestureSign.Daemon.Input
{
    public class TouchScreenDevice : HidDevice
    {
        public override Devices DeviceType => Devices.TouchScreen;

        public TouchScreenDevice(IntPtr rawInputBuffer, ref RAWINPUT raw) : base(rawInputBuffer, ref raw)
        {
        }

        public void GetRawDatas(short numberOfChildren, Screen currentScr, ref int requiringContactCount, ref List<RawData> _outputTouchs)
        {
            for (int dwIndex = 0; dwIndex < _dwCount; dwIndex++)
            {
                IntPtr pRawDataPacket = new IntPtr(_pRawData.ToInt64() + dwIndex * _dwSizHid);
                for (short nodeIndex = 1; nodeIndex <= numberOfChildren; nodeIndex++)
                {
                    int contactIdentifier = GetContactId(nodeIndex, pRawDataPacket);
                    Point point = GetCoordinate(nodeIndex, currentScr, pRawDataPacket);

                    // A HID raw-input message may contain multiple reports. Read
                    // the button usages from the report currently being parsed;
                    // using _pRawData here repeatedly inspected only the first
                    // report and caused some touchscreens to lose contact state.
                    ushort[] usageList = GetButtonList(_hPreparsedData.DangerousGetHandle(), pRawDataPacket, nodeIndex, _dwSizHid);
                    bool tip = Array.IndexOf(usageList, NativeMethods.TipId) >= 0;

                    _outputTouchs.Add(new RawData(tip ? DeviceStates.Tip : DeviceStates.None, contactIdentifier, point));

                    if (--requiringContactCount == 0) break;
                }
                if (requiringContactCount == 0) break;
            }
        }
    }
}
