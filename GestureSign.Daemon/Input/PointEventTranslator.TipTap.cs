using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Common.Plugins;
using GestureSign.Foundation;
using ManagedWinapi.Windows;

namespace GestureSign.Daemon.Input
{
    public partial class PointEventTranslator
    {
        private readonly object _tipTapLock = new object();
        private readonly TouchPadTipTapRecognizer _tipTap = new TouchPadTipTapRecognizer();
        private bool _tipTapOwned;
        private Point _tipTapPoint;
        private SystemWindow _tipTapTarget;

        internal bool ResetTipTap()
        {
            // Some hook regression tests create an uninitialized translator.
            if (_tipTapLock == null) return false;
            lock (_tipTapLock)
            {
                var owned = _tipTapOwned;
                _tipTap.Reset();
                _tipTapOwned = false;
                _tipTapTarget = null;
                return owned;
            }
        }

        private bool TranslateTipTap(RawPointsDataMessageEventArgs e)
        {
            lock (_tipTapLock)
            {
                var capture = PointCapture.Instance;
                if (!AppConfig.RegisterTouchPad || capture.Mode != CaptureMode.Normal ||
                    capture.State == CaptureState.Disabled ||
                    (SourceDevice != Devices.None && SourceDevice != Devices.TouchPad))
                { ResetTipTap(); return false; }

                var raw = e.RawData ?? new List<RawData>();
                var bounds = Screen.FromPoint(Cursor.Position).Bounds;
                TipTapContact Normalize(RawData p) => new TipTapContact(p.ContactIdentifier,
                    (p.RawPoints.X - bounds.Left) / (double)Math.Max(1, bounds.Width),
                    (p.RawPoints.Y - bounds.Top) / (double)Math.Max(1, bounds.Height));
                var active = raw.Where(p => p.State != DeviceStates.None)
                    .GroupBy(p => p.ContactIdentifier).Select(g => Normalize(g.Last())).ToList();
                var released = raw.Where(p => p.State == DeviceStates.None).Select(Normalize).ToList();
                var result = _tipTap.Update(active, Environment.TickCount64, released);
                if (result.Started != null)
                {
                    Logging.LogMessage($"TipTap candidate. Gesture={result.Started}, Held={result.HeldCount}, Contacts={string.Join(";", active.Select(c => $"{c.Id}:{c.X:F3},{c.Y:F3}"))}");
                    _tipTapPoint = Cursor.Position;
                    _tipTapTarget = ApplicationManager.Instance.GetTouchPadTarget(_tipTapPoint);
                }
                if (result.Recognized != null && _tipTapTarget != null)
                {
                    var actions = ApplicationManager.Instance.PrepareTouchPadTipTap(_tipTapTarget, result.Recognized, result.HeldCount + 1);
                    if (actions.Count > 0)
                    {
                        // Consume the ordinary capture before its partial-up event
                        // could also execute a two-finger tap or an edge action.
                        capture.CancelTouchPadForTipTap();
                        _tipTapOwned = true;
                        var points = Enumerable.Repeat(_tipTapPoint, result.HeldCount + 1).ToList();
                        PluginManager.Instance.ExecuteAction(actions, CaptureMode.Normal, Devices.TouchPad,
                            Enumerable.Range(0, points.Count).ToList(), points,
                            points.Select(p => new List<Point> { p }).ToList());
                        Logging.LogMessage($"TipTap recognized. Gesture={result.Recognized}, Held={result.HeldCount}, Actions={actions.Count}, TargetHwnd={_tipTapTarget.HWnd}");
                    }
                }
                if (!_tipTapOwned) return false;
                // After the first firing, keep the remaining held finger out of
                // ordinary drawing until every finger lifts. Repeated taps still run.
                SourceDevice = Devices.TouchPad;
                _lastPointsCount = active.Count;
                if (active.Count != 0) ArmTouchPadRelease(Devices.TouchPad, raw);
                else
                {
                    ResetTipTap();
                    ResetTouchStateIfReleased(raw);
                    SourceDevice = Devices.None;
                }
                return true;
            }
        }
    }
}
