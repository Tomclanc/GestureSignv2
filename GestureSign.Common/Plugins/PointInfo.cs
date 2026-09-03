using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using ManagedWinapi.Windows;

namespace GestureSign.Common.Plugins
{
    public class PointInfo
    {
        #region Private Variables

        private List<Point> _pointLocation;
        private SystemWindow _targetWindow;
        private SynchronizationContext _syncContext;

        #endregion

        #region Constructors

        public PointInfo(List<Point> pointLocation, List<List<Point>> points, SystemWindow target, SynchronizationContext syncContext)
            : this(pointLocation, null, points, target, syncContext)
        {
        }

        public PointInfo(List<Point> pointLocation, List<Point> lastCapturedPoints, List<List<Point>> points, SystemWindow target, SynchronizationContext syncContext)
        {
            _pointLocation = pointLocation;
            LastCapturedPoints = lastCapturedPoints;
            Points = points;
            _targetWindow = target;
            _syncContext = syncContext;
        }

        #endregion

        #region Public Properties

        public List<Point> PointLocation
        {
            get { return _pointLocation; }
            set
            {
                _pointLocation = value;
            }
        }

        public IntPtr WindowHandle => _targetWindow?.HWnd ?? IntPtr.Zero;

        public SystemWindow Window
        {
            get
            {
                // Keep the window captured at gesture start. The foreground can
                // legitimately change while a gesture is being drawn (notably
                // protected/UWP windows such as Defender); re-resolving from the
                // point after that change can return Progman/the shell and send
                // the action to the wrong target. PluginManager is responsible
                // for explicitly activating this captured target when requested.
                return _targetWindow;
            }
        }

        public List<List<Point>> Points { get; set; }

        public List<Point> LastCapturedPoints { get; set; }

        #endregion

        #region Public Methods

        public void Invoke(Action action)
        {
            _syncContext.Send((o) => action.Invoke(), null);
        }

        #endregion
    }
}
