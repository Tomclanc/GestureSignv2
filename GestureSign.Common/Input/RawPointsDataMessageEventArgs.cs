using System;
using System.Collections.Generic;

namespace GestureSign.Common.Input
{
    public class RawPointsDataMessageEventArgs : EventArgs
    {
        #region Constructors

        public RawPointsDataMessageEventArgs(List<RawData> rawData, Devices device, bool completeContactFrame = false)
        {
            this.RawData = rawData;
            SourceDevice = device;
            CompleteContactFrame = completeContactFrame;
        }


        #endregion

        #region Public Properties

        public List<RawData> RawData { get; set; }
        public Devices SourceDevice { get; set; }
        // Set only after all parallel/hybrid HID reports for a frame are assembled.
        public bool CompleteContactFrame { get; }

        #endregion
    }
}
