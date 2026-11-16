using GestureSign.Common.Applications;
using GestureSign.Common.Input;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace GestureSign.Daemon.Triggers
{
    class ContinuousGestureTrigger : Trigger
    {
        private Point _startPoint;
        private float _motionThreshold;
        private Stopwatch _stopwatch = new Stopwatch();
        private List<Point> _lastPoints;
        private List<Point> _strokeStarts;

        public ContinuousGestureTrigger()
        {
            _motionThreshold = 20f * DpiHelper.GetSystemDpi() / 96f;

            PointCapture.Instance.PointCaptured += PointCapture_PointCaptured;
            PointCapture.Instance.CaptureEnded += PointCapture_CaptureEnded;
            PointCapture.Instance.CaptureStarted += (sender, e) => Reset();
            PointCapture.Instance.CaptureCanceled += (sender, e) => Reset();
            GestureSign.Common.Log.Logging.LogMessage("Continuous gesture policy. Backend=AllContactsTranslation");
        }

        private void PointCapture_CaptureEnded(object sender, System.EventArgs e)
            => Reset();

        private void Reset()
        {
            _stopwatch.Stop();
            _lastPoints = null;
            _strokeStarts = null;
        }

        private void PointCapture_PointCaptured(object sender, PointsCapturedEventArgs e)
        {
            if (PointCapture.Instance.State != CaptureState.Capturing || e.Points.Count < 2)
                return;
            var actionsWithContinuousGesture = ApplicationManager.Instance.GetRecognizedDefinedAction(a => a != null && a.ContinuousGesture != null);
            if (actionsWithContinuousGesture == null || actionsWithContinuousGesture.Count == 0)
                return;
            if (e.Points.Any(stroke => stroke == null || stroke.Count == 0))
                return;
            // Use the stable trajectory order, not the raw packet order. A raw
            // frame can reorder contacts, and touchpad FirstCapturedPoints are
            // visual origins rather than the current finger positions.
            var current = e.Points.Select(stroke => stroke[stroke.Count - 1]).ToList();
            var starts = e.Points.Select(stroke => stroke[0]).ToList();
            if (_lastPoints == null || !_strokeStarts.SequenceEqual(starts))
            {
                _startPoint = current[0];
                _lastPoints = current;
                _strokeStarts = starts;
                _stopwatch.Restart();
                return;
            }

            // A moving centroid is not enough: one moving finger with two
            // stationary anchors must not become a three-finger swipe.
            if (!TryGetTranslation(_lastPoints, current, out int deltaX, out int deltaY))
                return;
            int deltaXAbs = Math.Abs(deltaX);
            int deltaYAbs = Math.Abs(deltaY);
            bool isHorizontal = deltaXAbs > deltaYAbs;
            if (isHorizontal)
            {
                var rate = GetRateOfFire(deltaXAbs);
                if (rate >= 1)
                {
                    for (int i = 1; i < rate; i++)
                    {
                        OnGesturerRecognized(_lastPoints.Count, deltaX > 0 ? Gestures.Right : Gestures.Left);
                    }
                    _stopwatch.Restart();
                    _lastPoints = current;
                }
            }
            else
            {
                var rate = GetRateOfFire(deltaYAbs);
                if (rate >= 1)
                {
                    for (int i = 1; i < rate; i++)
                    {
                        OnGesturerRecognized(_lastPoints.Count, deltaY > 0 ? Gestures.Down : Gestures.Up);
                    }
                    _stopwatch.Restart();
                    _lastPoints = current;
                }
            }
        }

        internal static bool TryGetTranslation(IReadOnlyList<Point> previous, IReadOnlyList<Point> current,
            out int deltaX, out int deltaY)
        {
            deltaX = deltaY = 0;
            if (previous == null || current == null || previous.Count < 2 || previous.Count != current.Count)
                return false;

            int firstX = current[0].X - previous[0].X;
            int firstY = current[0].Y - previous[0].Y;
            bool horizontal = Math.Abs(firstX) > Math.Abs(firstY);
            int direction = Math.Sign(horizontal ? firstX : firstY);
            if (direction == 0) return false;
            int distance = int.MaxValue;
            for (int i = 0; i < previous.Count; i++)
            {
                int x = current[i].X - previous[i].X;
                int y = current[i].Y - previous[i].Y;
                int along = horizontal ? x : y;
                int across = horizontal ? y : x;
                if (Math.Sign(along) != direction || Math.Abs(along) <= Math.Abs(across))
                    return false;
                // Every selected finger must cover the firing distance. Small
                // anchor jitter cannot borrow distance from the moving finger.
                distance = Math.Min(distance, Math.Abs(along));
            }
            if (horizontal) deltaX = direction * distance;
            else deltaY = direction * distance;
            return true;
        }

        private void OnGesturerRecognized(int contactCount, Gestures gesture)
        {
            var actions = ApplicationManager.Instance.GetRecognizedDefinedAction(a => a.ContinuousGesture != null &&
            a.ContinuousGesture.ContactCount == contactCount && a.ContinuousGesture.Gesture == gesture);
            if (actions.Count > 0)
                OnTriggerFired(new TriggerFiredEventArgs(actions, _startPoint));
        }

        private double GetRateOfFire(int distance)
        {
            var deltaTime = _stopwatch.ElapsedMilliseconds;
            if (deltaTime < 2)
                return 0;

            var velocity = distance / (double)deltaTime;
            if (velocity < 3)
            {
                return distance / _motionThreshold;
            }
            else
            {
                if (velocity > 16)
                    velocity = 16;
                return distance / ((-0.0023 * velocity * velocity + 0.0096 * velocity + 0.89) * _motionThreshold);
            }
        }
    }
}
