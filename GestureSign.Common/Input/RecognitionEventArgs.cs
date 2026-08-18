using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GestureSign.Common.Input
{
    public class RecognitionEventArgs : EventArgs
    {
        #region Constructors

        public RecognitionEventArgs(List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers)
            : this(points, capturePoints, contactIdentifiers, null)
        {
        }

        public RecognitionEventArgs(List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers, List<int> contactOrder)
            : this(points, capturePoints, contactIdentifiers, contactOrder, null)
        {
        }

        public RecognitionEventArgs(List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers, List<int> contactOrder, List<Point> lastCapturedPoints)
        {
            this.Points = points;
            this.FirstCapturedPoints = capturePoints;
            ContactIdentifiers = contactIdentifiers;
            ContactOrder = contactOrder;
            LastCapturedPoints = lastCapturedPoints;
        }

        public RecognitionEventArgs(string gestureName, List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers)
            : this(points, capturePoints, contactIdentifiers)
        {
            this.GestureName = gestureName;
        }

        public RecognitionEventArgs(string gestureName, List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers, List<int> contactOrder)
            : this(points, capturePoints, contactIdentifiers, contactOrder)
        {
            this.GestureName = gestureName;
        }

        public RecognitionEventArgs(string gestureName, List<List<Point>> points, List<Point> capturePoints, List<int> contactIdentifiers, List<int> contactOrder, List<Point> lastCapturedPoints)
            : this(points, capturePoints, contactIdentifiers, contactOrder, lastCapturedPoints)
        {
            this.GestureName = gestureName;
        }

        #endregion

        #region Public Instance Properties

        public string GestureName { get; set; }
        public List<List<Point>> Points { get; set; }
        public List<Point> FirstCapturedPoints { get; set; }
        public List<int> ContactIdentifiers { get; set; }
        public List<int> ContactOrder { get; set; }
        public List<Point> LastCapturedPoints { get; set; }

        #endregion
    }
}
