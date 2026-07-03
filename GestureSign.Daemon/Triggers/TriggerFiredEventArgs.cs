using GestureSign.Common.Applications;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace GestureSign.Daemon.Triggers
{
    public class TriggerFiredEventArgs : EventArgs
    {
        public TriggerFiredEventArgs(List<IAction> firedActions, Point firedPoint)
            : this(firedActions, firedPoint, null)
        {
        }

        public TriggerFiredEventArgs(List<IAction> firedActions, Point firedPoint, List<List<Point>> points)
        {
            FiredActions = firedActions;
            FiredPoint = firedPoint;
            Points = points;
        }

        public List<IAction> FiredActions { get; }
        public Point FiredPoint { get; }
        public List<List<Point>> Points { get; }
    }
}
