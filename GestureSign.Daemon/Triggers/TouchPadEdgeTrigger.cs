using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Daemon.Input;
using GestureSign.PointPatterns;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace GestureSign.Daemon.Triggers
{
    class TouchPadEdgeTrigger : Trigger
    {
        public const string TopGestureName = "TouchPadEdge.Top";
        public const string BottomGestureName = "TouchPadEdge.Bottom";
        public const string LeftGestureName = "TouchPadEdge.Left";
        public const string RightGestureName = "TouchPadEdge.Right";
        public const string TopLeftGestureName = "TouchPadEdge.Top.Left";
        public const string TopRightGestureName = "TouchPadEdge.Top.Right";
        public const string BottomLeftGestureName = "TouchPadEdge.Bottom.Left";
        public const string BottomRightGestureName = "TouchPadEdge.Bottom.Right";
        public const string LeftUpGestureName = "TouchPadEdge.Left.Up";
        public const string LeftDownGestureName = "TouchPadEdge.Left.Down";
        public const string RightUpGestureName = "TouchPadEdge.Right.Up";
        public const string RightDownGestureName = "TouchPadEdge.Right.Down";

        private const int EdgePercent = 8;
        private const int MaxTapTravel = 35;
        private const int MinSwipeTravel = 90;
        private readonly Devices _sourceDevice;
        private readonly string _gesturePrefix;
        private readonly string _logPrefix;
        private PendingEdgeTrigger _pendingEdgeTrigger;

        public TouchPadEdgeTrigger()
            : this(Devices.TouchPad, "TouchPadEdge", "TouchPad")
        {
        }

        public TouchPadEdgeTrigger(Devices sourceDevice, string gesturePrefix, string logPrefix)
        {
            _sourceDevice = sourceDevice;
            _gesturePrefix = gesturePrefix;
            _logPrefix = logPrefix;
            PointCapture.Instance.CaptureStarted += PointCapture_CaptureStarted;
            PointCapture.Instance.BeforePointsCaptured += PointCapture_BeforePointsCaptured;
        }

        private void PointCapture_CaptureStarted(object sender, PointsCapturedEventArgs e)
        {
            _pendingEdgeTrigger = null;

            var pointCapture = PointCapture.Instance;
            if (pointCapture.Mode == CaptureMode.Training || pointCapture.SourceDevice != _sourceDevice)
                return;

            if (e.Points == null || e.Points.Count != 1 || e.Points[0].Count == 0)
                return;

            var edge = GetStartEdge(e.Points[0].First());
            if (edge == null)
            {
                Logging.LogMessage($"{_logPrefix} edge capture ignored. Reason=NotOnEdge, Point={FormatPoint(e.Points[0].First())}");
                return;
            }

            ApplicationManager.Instance.GetForegroundApplications();
            var hasAnyAction = GetCandidateGestureNames(edge.Value)
                .Any(name => ApplicationManager.Instance.GetRecognizedDefinedAction(name)?.Any() == true);
            if (!hasAnyAction)
            {
                Logging.LogMessage($"{_logPrefix} edge capture ignored. Reason=NoAction, Edge={edge}, Point={FormatPoint(e.Points[0].First())}");
                return;
            }

            _pendingEdgeTrigger = new PendingEdgeTrigger(edge.Value, e.FirstCapturedPoints.FirstOrDefault());
            e.Cancel = false;
            e.BlockTouchInputThreshold = 0;
            Logging.LogMessage($"{_logPrefix} edge capture accepted. Edge={edge}, Point={FormatPoint(e.Points[0].First())}");
        }

        private void PointCapture_BeforePointsCaptured(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = PointCapture.Instance;
            if (pointCapture.Mode == CaptureMode.Training || pointCapture.SourceDevice != _sourceDevice)
                return;

            if (_pendingEdgeTrigger != null)
            {
                var pendingGestureName = e.Points == null || e.Points.Count != 1 || e.Points[0].Count == 0
                    ? null
                    : GetEdgeGestureName(_pendingEdgeTrigger.Edge, e.Points[0]);
                if (pendingGestureName == null)
                {
                    Logging.LogMessage($"{_logPrefix} edge trigger canceled. Edge={_pendingEdgeTrigger.Edge}, Reason=NoTapOrSwipe");
                    _pendingEdgeTrigger = null;
                    return;
                }

                ApplicationManager.Instance.GetForegroundApplications();
                var pendingActions = ApplicationManager.Instance.GetRecognizedDefinedAction(pendingGestureName)?.ToList();
                if (pendingActions == null || pendingActions.Count == 0)
                {
                    Logging.LogMessage($"{_logPrefix} edge trigger canceled. Edge={pendingGestureName}, Reason=NoAction");
                    _pendingEdgeTrigger = null;
                    return;
                }

                Logging.LogMessage($"{_logPrefix} edge trigger fired. Edge={pendingGestureName}, Actions={pendingActions.Count}");
                e.Cancel = true;
                OnTriggerFired(new TriggerFiredEventArgs(pendingActions, _pendingEdgeTrigger.FiredPoint));
                _pendingEdgeTrigger = null;
                return;
            }

            var edgeGestureName = GetEdgeGestureName(e.Points[0]);
            if (edgeGestureName == null)
                return;

            ApplicationManager.Instance.GetForegroundApplications();
            var actions = ApplicationManager.Instance.GetRecognizedDefinedAction(edgeGestureName)?.ToList();
            if (actions == null || actions.Count == 0)
                return;

            Logging.LogMessage($"{_logPrefix} edge trigger fired. Edge={edgeGestureName}, Actions={actions.Count}");
            e.Cancel = true;
            OnTriggerFired(new TriggerFiredEventArgs(actions, e.FirstCapturedPoints.FirstOrDefault()));
        }

        private string GetEdgeGestureName(List<Point> points)
        {
            var edge = GetStartEdge(points.First());
            return edge == null ? null : GetEdgeGestureName(edge.Value, points);
        }

        private static Edge? GetStartEdge(Point start)
        {
            var bounds = Screen.FromPoint(start).Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            var x = start.X - bounds.Left;
            var y = start.Y - bounds.Top;
            var edgeWidth = Math.Max(1, bounds.Width * EdgePercent / 100);
            var edgeHeight = Math.Max(1, bounds.Height * EdgePercent / 100);

            var top = y <= edgeHeight;
            var bottom = y >= bounds.Height - edgeHeight;
            var left = x <= edgeWidth;
            var right = x >= bounds.Width - edgeWidth;

            if (top && !left && !right)
                return Edge.Top;
            if (bottom && !left && !right)
                return Edge.Bottom;
            if (left && !top && !bottom)
                return Edge.Left;
            if (right && !top && !bottom)
                return Edge.Right;

            return null;
        }

        private string GetEdgeGestureName(Edge edge, List<Point> points)
        {
            var start = points.First();
            var end = points.Last();
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            if (PointPatternMath.GetDistance(start, end) <= MaxTapTravel)
                return GetTapGestureName(edge);

            switch (edge)
            {
                case Edge.Top:
                    if (IsHorizontalSwipe(dx, dy))
                        return dx < 0 ? $"{_gesturePrefix}.Top.Left" : $"{_gesturePrefix}.Top.Right";
                    break;
                case Edge.Bottom:
                    if (IsHorizontalSwipe(dx, dy))
                        return dx < 0 ? $"{_gesturePrefix}.Bottom.Left" : $"{_gesturePrefix}.Bottom.Right";
                    break;
                case Edge.Left:
                    if (IsVerticalSwipe(dx, dy))
                        return dy < 0 ? $"{_gesturePrefix}.Left.Up" : $"{_gesturePrefix}.Left.Down";
                    break;
                case Edge.Right:
                    if (IsVerticalSwipe(dx, dy))
                        return dy < 0 ? $"{_gesturePrefix}.Right.Up" : $"{_gesturePrefix}.Right.Down";
                    break;
            }

            return null;
        }

        private static bool IsHorizontalSwipe(int dx, int dy)
        {
            return Math.Abs(dx) >= MinSwipeTravel && Math.Abs(dx) > Math.Abs(dy) * 1.5;
        }

        private static bool IsVerticalSwipe(int dx, int dy)
        {
            return Math.Abs(dy) >= MinSwipeTravel && Math.Abs(dy) > Math.Abs(dx) * 1.5;
        }

        private string GetTapGestureName(Edge edge)
        {
            switch (edge)
            {
                case Edge.Top:
                    return $"{_gesturePrefix}.Top";
                case Edge.Bottom:
                    return $"{_gesturePrefix}.Bottom";
                case Edge.Left:
                    return $"{_gesturePrefix}.Left";
                case Edge.Right:
                    return $"{_gesturePrefix}.Right";
                default:
                    return null;
            }
        }

        private IEnumerable<string> GetCandidateGestureNames(Edge edge)
        {
            yield return GetTapGestureName(edge);
            switch (edge)
            {
                case Edge.Top:
                    yield return $"{_gesturePrefix}.Top.Left";
                    yield return $"{_gesturePrefix}.Top.Right";
                    break;
                case Edge.Bottom:
                    yield return $"{_gesturePrefix}.Bottom.Left";
                    yield return $"{_gesturePrefix}.Bottom.Right";
                    break;
                case Edge.Left:
                    yield return $"{_gesturePrefix}.Left.Up";
                    yield return $"{_gesturePrefix}.Left.Down";
                    break;
                case Edge.Right:
                    yield return $"{_gesturePrefix}.Right.Up";
                    yield return $"{_gesturePrefix}.Right.Down";
                    break;
            }
        }

        private static string FormatPoint(Point point)
        {
            return $"{point.X},{point.Y}";
        }

        private enum Edge
        {
            Top,
            Bottom,
            Left,
            Right
        }

        private class PendingEdgeTrigger
        {
            public PendingEdgeTrigger(Edge edge, Point firedPoint)
            {
                Edge = edge;
                FiredPoint = firedPoint;
            }

            public Edge Edge { get; }
            public Point FiredPoint { get; }
        }
    }
}
