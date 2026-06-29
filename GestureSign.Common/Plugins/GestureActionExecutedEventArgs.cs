using System;
using GestureSign.Common.Input;

namespace GestureSign.Common.Plugins
{
    public class GestureActionExecutedEventArgs : EventArgs
    {
        public GestureActionExecutedEventArgs(string actionName, string gestureName, Devices devices)
        {
            ActionName = actionName;
            GestureName = gestureName;
            Devices = devices;
        }

        public string ActionName { get; }

        public string GestureName { get; }

        public Devices Devices { get; }
    }
}
