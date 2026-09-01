using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Daemon.Input;
using GestureSign.PointPatterns;
using Newtonsoft.Json.Linq;
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
        private const int ContinuousActionStep = 36;
        private const int CaptionButtonWidth = 180;
        private const int CaptionButtonHeight = 72;
        private readonly Devices _sourceDevice;
        private readonly string _gesturePrefix;
        private readonly string _logPrefix;
        private readonly int _edgePercent;
        private readonly int _maxTapTravel;
        private readonly int _minSwipeTravel;
        private readonly double _swipeDominanceRatio;
        private readonly bool _allowCornerEdges;
        private PendingEdgeTrigger _pendingEdgeTrigger;
        private Point? _continuousActionAnchor;
        private bool _continuousActionFired;

        public TouchPadEdgeTrigger()
            : this(Devices.TouchPad, "TouchPadEdge", "TouchPad", EdgePercent, MaxTapTravel, MinSwipeTravel, 1.5, false)
        {
        }

        public TouchPadEdgeTrigger(Devices sourceDevice, string gesturePrefix, string logPrefix)
            : this(sourceDevice, gesturePrefix, logPrefix, EdgePercent, MaxTapTravel, MinSwipeTravel, 1.5, false)
        {
        }

        public TouchPadEdgeTrigger(Devices sourceDevice, string gesturePrefix, string logPrefix, int edgePercent, int maxTapTravel, int minSwipeTravel, double swipeDominanceRatio, bool allowCornerEdges)
        {
            _sourceDevice = sourceDevice;
            _gesturePrefix = gesturePrefix;
            _logPrefix = logPrefix;
            _edgePercent = edgePercent;
            _maxTapTravel = maxTapTravel;
            _minSwipeTravel = minSwipeTravel;
            _swipeDominanceRatio = swipeDominanceRatio;
            _allowCornerEdges = allowCornerEdges;
            PointCapture.Instance.CaptureStarted += PointCapture_CaptureStarted;
            PointCapture.Instance.PointCaptured += PointCapture_PointCaptured;
            PointCapture.Instance.BeforePointsCaptured += PointCapture_BeforePointsCaptured;
        }

        private void PointCapture_CaptureStarted(object sender, PointsCapturedEventArgs e)
        {
            _pendingEdgeTrigger = null;
            _continuousActionAnchor = null;
            _continuousActionFired = false;

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
            e.ForceCapture = true;
            e.RequiredContactCount = 1;
            e.BlockTouchInputThreshold = 0;
            Logging.LogMessage($"{_logPrefix} edge capture accepted. Edge={edge}, Point={FormatPoint(e.Points[0].First())}");
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            if (_pendingEdgeTrigger == null ||
                PointCapture.Instance.Mode == CaptureMode.Training ||
                PointCapture.Instance.SourceDevice != _sourceDevice ||
                e?.Points == null || e.Points.Count != 1 ||
                e.Points[0] == null || e.Points[0].Count == 0)
            {
                return;
            }

            var edge = _pendingEdgeTrigger.Edge;
            var stroke = e.Points[0];
            var start = stroke.First();
            var current = stroke.Last();
            var totalDx = current.X - start.X;
            var totalDy = current.Y - start.Y;
            var isVerticalEdge = edge == Edge.Left || edge == Edge.Right;
            if (isVerticalEdge ? !IsVerticalSwipe(totalDx, totalDy) : !IsHorizontalSwipe(totalDx, totalDy))
                return;

            if (_continuousActionAnchor == null)
                _continuousActionAnchor = start;

            var delta = isVerticalEdge
                ? current.Y - _continuousActionAnchor.Value.Y
                : current.X - _continuousActionAnchor.Value.X;
            var direction = isVerticalEdge
                ? (delta < 0 ? "Up" : "Down")
                : (delta < 0 ? "Left" : "Right");
            var gestureName = $"{_gesturePrefix}.{edge}.{direction}";
            var actions = ApplicationManager.Instance.GetRecognizedDefinedAction(gestureName)?
                .Where(action => IsContinuousEdgeAction(action, isVerticalEdge))
                .ToList();
            if (actions == null || actions.Count == 0)
                return;

            var fireCount = Math.Min(4, Math.Abs(delta) / ContinuousActionStep);
            if (fireCount <= 0)
                return;

            var step = Math.Sign(delta) * ContinuousActionStep;
            for (var index = 0; index < fireCount; index++)
            {
                OnTriggerFired(new TriggerFiredEventArgs(actions, _pendingEdgeTrigger.FiredPoint, ClonePoints(e.Points)));
                _continuousActionAnchor = isVerticalEdge
                    ? new Point(_continuousActionAnchor.Value.X, _continuousActionAnchor.Value.Y + step)
                    : new Point(_continuousActionAnchor.Value.X + step, _continuousActionAnchor.Value.Y);
            }

            _continuousActionFired = true;
            Logging.LogMessage($"{_logPrefix} edge action fired continuously. Edge={gestureName}, Steps={fireCount}");
        }

        private static bool IsContinuousEdgeAction(IAction action, bool isVerticalEdge)
        {
            if (action?.Commands == null)
                return false;

            var command = action.Commands.FirstOrDefault(item => item != null && item.IsEnabled);
            if (command == null || string.IsNullOrWhiteSpace(command.PluginClass))
                return false;

            if (command.PluginClass.IndexOf("Volume", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                try
                {
                    var settings = JObject.Parse(command.CommandSettings ?? "{}");
                    var method = settings.Value<int?>("Method") ?? 0;
                    // Builds that introduced continuous edge volume predate
                    // this setting, so a missing value remains continuous.
                    return method != 2 && (settings.Value<bool?>("ContinuousEdge") ?? true);
                }
                catch
                {
                    return true;
                }
            }

            if (command.PluginClass.IndexOf("MouseActions", StringComparison.OrdinalIgnoreCase) < 0)
                return false;

            try
            {
                var settings = JObject.Parse(command.CommandSettings ?? "{}");
                var mouseAction = settings.Value<int?>("MouseAction") ?? 0;
                return isVerticalEdge ? mouseAction == 4096 : mouseAction == 8192;
            }
            catch
            {
                return false;
            }
        }

        private void PointCapture_BeforePointsCaptured(object sender, PointsCapturedEventArgs e)
        {
            var pointCapture = PointCapture.Instance;
            if (pointCapture.Mode == CaptureMode.Training || pointCapture.SourceDevice != _sourceDevice)
                return;

            // A touchpad contact can be released while the raw-input idle
            // timer is finishing another capture (notably after a two-finger
            // tap on the tray icon). In that race the event may contain no
            // strokes. Edge recognition must treat it as a non-match instead
            // of indexing e.Points[0] and terminating the daemon.
            if (e?.Points == null || e.Points.Count == 0 || e.Points[0] == null || e.Points[0].Count == 0)
            {
                if (_pendingEdgeTrigger != null)
                    Logging.LogMessage($"{_logPrefix} edge trigger canceled. Reason=EmptyPoints");
                _pendingEdgeTrigger = null;
                return;
            }

            if (_pendingEdgeTrigger != null)
            {
                if (_continuousActionFired)
                {
                    Logging.LogMessage($"{_logPrefix} edge trigger completed after continuous action. Edge={_pendingEdgeTrigger.Edge}");
                    e.Cancel = true;
                    _pendingEdgeTrigger = null;
                    _continuousActionAnchor = null;
                    _continuousActionFired = false;
                    return;
                }

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
                OnTriggerFired(new TriggerFiredEventArgs(pendingActions, _pendingEdgeTrigger.FiredPoint, ClonePoints(e.Points)));
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
            OnTriggerFired(new TriggerFiredEventArgs(actions, e.FirstCapturedPoints.FirstOrDefault(), ClonePoints(e.Points)));
        }

        private static List<List<Point>> ClonePoints(IEnumerable<List<Point>> points)
        {
            return points?.Select(stroke => stroke?.ToList() ?? new List<Point>()).ToList();
        }

        private string GetEdgeGestureName(List<Point> points)
        {
            var edge = GetStartEdge(points.First());
            return edge == null ? null : GetEdgeGestureName(edge.Value, points);
        }

        private Edge? GetStartEdge(Point start)
        {
            var bounds = Screen.FromPoint(start).Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0)
                return null;

            var x = start.X - bounds.Left;
            var y = start.Y - bounds.Top;
            var edgeWidth = Math.Max(1, bounds.Width * _edgePercent / 100);
            var edgeHeight = Math.Max(1, bounds.Height * _edgePercent / 100);

            var top = y <= edgeHeight;
            var bottom = y >= bounds.Height - edgeHeight;
            var left = x <= edgeWidth;
            var right = x >= bounds.Width - edgeWidth;

            if ((_sourceDevice == Devices.TouchScreen || _sourceDevice == Devices.Mouse) && IsCaptionButtonRegion(bounds, x, y))
            {
                Logging.LogMessage($"{_logPrefix} edge capture ignored. Reason=CaptionButtonRegion, Point={FormatPoint(start)}");
                return null;
            }

            if (top && !left && !right)
                return Edge.Top;
            if (bottom && !left && !right)
                return Edge.Bottom;
            if (left && !top && !bottom)
                return Edge.Left;
            if (right && !top && !bottom)
                return Edge.Right;

            if (_allowCornerEdges)
            {
                if (top && left)
                    return y <= x ? Edge.Top : Edge.Left;
                if (top && right)
                    return y <= bounds.Width - x ? Edge.Top : Edge.Right;
                if (bottom && left)
                    return bounds.Height - y <= x ? Edge.Bottom : Edge.Left;
                if (bottom && right)
                    return bounds.Height - y <= bounds.Width - x ? Edge.Bottom : Edge.Right;
            }

            return null;
        }

        private string GetEdgeGestureName(Edge edge, List<Point> points)
        {
            var start = points.First();
            var end = points.Last();
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;
            if (PointPatternMath.GetDistance(start, end) <= _maxTapTravel)
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

        private bool IsHorizontalSwipe(int dx, int dy)
        {
            return Math.Abs(dx) >= _minSwipeTravel && Math.Abs(dx) > Math.Abs(dy) * _swipeDominanceRatio;
        }

        private bool IsVerticalSwipe(int dx, int dy)
        {
            return Math.Abs(dy) >= _minSwipeTravel && Math.Abs(dy) > Math.Abs(dx) * _swipeDominanceRatio;
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

        private static bool IsCaptionButtonRegion(Rectangle bounds, int x, int y)
        {
            return y <= CaptionButtonHeight &&
                   (x <= CaptionButtonWidth || x >= bounds.Width - CaptionButtonWidth);
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
