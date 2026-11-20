using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GestureSign.Common.Applications;
using GestureSign.Common.Configuration;
using GestureSign.Common.Input;
using GestureSign.Common.Log;
using GestureSign.Daemon.Input;
using GestureSign.Daemon.Native;
using GestureSign.Daemon.Triggers;
using Microsoft.Win32;
using WindowsInput;

namespace GestureSign.Daemon.RadialMenu
{
    internal sealed class RadialMenuHit
    {
        public RadialMenuHit(bool outer, int index, IAction action)
        {
            Outer = outer;
            Index = index;
            Action = action;
        }

        public bool Outer { get; }
        public int Index { get; }
        public IAction Action { get; }
    }

    internal sealed class RadialMenuLabel
    {
        public bool Outer { get; set; }
        public int Index { get; set; }
        public string Text { get; set; }
        public Bitmap Icon { get; set; }
        public string Glyph { get; set; }
    }

    /// <summary>Geometry of one opened radial menu, in physical screen pixels.</summary>
    internal sealed class RadialMenuLayout
    {
        public const int Slots = RadialMenuSlots.SlotsPerRing;
        private const float DeadZone96 = 34f;
        private const float Inner96 = 110f;
        private const float Outer96 = 176f;
        private const float Margin96 = 6f;

        private readonly Dictionary<string, IAction> _slots;

        public RadialMenuLayout(Dictionary<string, IAction> slots, Point origin)
        {
            _slots = slots;
            Scale = Math.Max(1f, DpiHelper.GetScreenDpi(origin) / 96f);
            HasOuter = AppConfig.RadialMenuOuterRing;
            DeadZoneRadius = DeadZone96 * Scale;
            InnerRadius = Inner96 * Scale;
            OuterRadius = Outer96 * Scale;
            var radius = (HasOuter ? OuterRadius : InnerRadius) + Margin96 * Scale;
            WindowSize = (int)Math.Ceiling(radius * 2);

            // Keep the whole menu on the monitor. Selection is measured from the
            // drawn center, so a shifted menu still behaves consistently.
            var screen = Screen.FromPoint(origin).Bounds;
            var half = WindowSize / 2;
            var x = Math.Min(Math.Max(origin.X, screen.Left + half), screen.Right - half);
            var y = Math.Min(Math.Max(origin.Y, screen.Top + half), screen.Bottom - half);
            Center = new Point(x, y);

            Labels = slots
                .Select(pair => new { Hit = Parse(pair.Key, pair.Value) })
                .Where(x2 => x2.Hit != null)
                .Select(x2 => new RadialMenuLabel
                {
                    Outer = x2.Hit.Outer,
                    Index = x2.Hit.Index,
                    Text = LabelOf(x2.Hit.Action),
                    Icon = RadialMenuIcons.FileIcon(x2.Hit.Action),
                    Glyph = RadialMenuIcons.Glyph(x2.Hit.Action)
                })
                .ToList();
        }

        public float Scale { get; }
        public bool HasOuter { get; }
        public float DeadZoneRadius { get; }
        public float InnerRadius { get; }
        public float OuterRadius { get; }
        public int WindowSize { get; }
        public Point Center { get; }
        public List<RadialMenuLabel> Labels { get; }

        /// <summary>Angle of a slot's center, clockwise from 12 o'clock, in radians.</summary>
        public static double SlotAngle(int index) => index * 2 * Math.PI / Slots;

        /// <summary>
        /// Picks a slot by direction, then by distance (near = inner ring, far =
        /// outer ring). An empty slot selects nothing.
        /// </summary>
        public RadialMenuHit HitTest(Point cursor)
        {
            double dx = cursor.X - Center.X;
            double dy = cursor.Y - Center.Y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < DeadZoneRadius)
                return null;

            var angle = Math.Atan2(dx, -dy);
            if (angle < 0) angle += 2 * Math.PI;
            var index = (int)Math.Round(angle / (2 * Math.PI / Slots)) % Slots;

            return Find(HasOuter && distance >= InnerRadius, index);
        }

        private RadialMenuHit Find(bool outer, int index)
            => _slots.TryGetValue(RadialMenuSlots.Name(outer, index), out var action) ? new RadialMenuHit(outer, index, action) : null;

        private static RadialMenuHit Parse(string name, IAction action)
        {
            foreach (var outer in new[] { false, true })
                for (var i = 0; i < Slots; i++)
                    if (string.Equals(name, RadialMenuSlots.Name(outer, i), StringComparison.OrdinalIgnoreCase))
                        return new RadialMenuHit(outer, i, action);
            return null;
        }

        private static string LabelOf(IAction action)
        {
            var text = action?.Name;
            if (RadialMenuIcons.IsGenericLabel(text))
                text = action?.Commands?.FirstOrDefault(c => c != null && c.IsEnabled)?.Name;
            if (RadialMenuIcons.IsGenericLabel(text))
                text = RadialMenuIcons.AutoLabel(action);
            return string.IsNullOrWhiteSpace(text) ? "?" : text.Trim();
        }

        public static bool IsDarkTheme()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>Fires the selected slot through the normal trigger pipeline.</summary>
    internal sealed class RadialMenuTrigger : Trigger
    {
        public RadialMenuTrigger()
        {
            RadialMenuController.Instance.Trigger = this;
        }

        internal void Fire(IAction action, Point point)
        {
            OnTriggerFired(new TriggerFiredEventArgs(new List<IAction> { action }, point) { SourceDevice = Devices.Mouse });
        }
    }

    /// <summary>
    /// Middle-button radial menu: press the middle button (pending), drag more
    /// than 8 px to open the menu around the press point, release to run the
    /// highlighted slot. Releasing without dragging replays a native middle
    /// click; releasing in the center cancels. Runs on the hook thread.
    /// </summary>
    internal sealed class RadialMenuController
    {
        private const int DragThreshold96 = 8;
        private const int VK_MBUTTON = 0x04;
        private const int WatchdogIntervalMs = 15;
        private const int MaximumHoldMs = 15000;

        public static RadialMenuController Instance { get; } = new RadialMenuController();

        private Dictionary<string, IAction> _slots;
        private RadialMenuLayout _layout;
        private RadialMenuForm _form;
        private Timer _timer;
        private Point _origin;
        private Point _cursor;
        private RadialMenuHit _selected;
        private bool _open;
        private bool _dirty;
        private DateTime _beganUtc;
        private DateTime _lastHookUtc;
        private int _releasedTicks;
        private bool _observedDown;

        internal RadialMenuTrigger Trigger { get; set; }

        public bool IsActive { get; private set; }

        /// <summary>Called on middle button down. Returns true when the press is owned by the radial menu.</summary>
        public bool TryBegin(Point point)
        {
            if (IsActive || Trigger == null)
                return false;
            if (PointCapture.Instance.Mode != CaptureMode.Normal || PointCapture.Instance.State != CaptureState.Ready)
                return false;

            Dictionary<string, IAction> slots;
            try
            {
                slots = ApplicationManager.Instance.PrepareRadialMenu(point);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                return false;
            }
            if (slots == null)
                return false;

            _slots = slots;
            _layout = null;
            _origin = point;
            _cursor = point;
            _selected = null;
            _open = false;
            _dirty = false;
            _releasedTicks = 0;
            _observedDown = false;
            _beganUtc = _lastHookUtc = DateTime.UtcNow;
            IsActive = true;
            EnsureTimer();
            _timer.Start();
            Logging.LogMessage($"Radial menu pending. Point={point.X},{point.Y}, Slots={slots.Count}");
            return true;
        }

        public void Move(Point point)
        {
            if (!IsActive)
                return;
            _lastHookUtc = DateTime.UtcNow;
            _cursor = point;
            if (!_open)
            {
                var threshold = DragThreshold96 * Math.Max(1f, DpiHelper.GetScreenDpi(_origin) / 96f);
                var dx = point.X - _origin.X;
                var dy = point.Y - _origin.Y;
                if (dx * dx + dy * dy <= threshold * threshold)
                    return;
                _open = true;
                _layout = new RadialMenuLayout(_slots, _origin);
                Logging.LogMessage($"Radial menu opened. Center={_layout.Center.X},{_layout.Center.Y}, Outer={_layout.HasOuter}, Scale={_layout.Scale:0.##}");
                // Draw the first frame immediately instead of waiting for the next
                // timer tick, so the menu appears the moment the drag starts.
                _selected = _layout.HitTest(point);
                _dirty = false;
                _form.Render(_layout, _cursor, _selected);
                return;
            }
            _selected = _layout.HitTest(point);
            _dirty = true;
        }

        public void End(Point point)
        {
            if (!IsActive)
                return;
            Move(point);
            var wasOpen = _open;
            var selected = wasOpen ? _selected : null;
            var origin = _origin;
            Reset();

            if (!wasOpen)
            {
                Logging.LogMessage("Radial menu released without dragging; replaying native middle click.");
                Task.Run(() =>
                {
                    try { new InputSimulator().Mouse.MiddleButtonClick(); }
                    catch (Exception ex) { Logging.LogException(ex); }
                });
                return;
            }

            if (selected?.Action == null)
            {
                Logging.LogMessage("Radial menu canceled. Reason=NoSelection");
                return;
            }

            Logging.LogMessage($"Radial menu selected. Slot={RadialMenuSlots.Name(selected.Outer, selected.Index)}, Action={selected.Action.Name}");
            var trigger = Trigger;
            // Let the hook return and the overlay disappear before commands run,
            // so screenshots and key sends see the real target window.
            _form?.BeginInvoke(new global::System.Action(() => trigger?.Fire(selected.Action, origin)));
        }

        public void Cancel(string reason)
        {
            if (!IsActive)
                return;
            Logging.LogMessage($"Radial menu canceled. Reason={reason}");
            Reset();
        }

        private void Reset()
        {
            IsActive = false;
            _open = false;
            _dirty = false;
            _selected = null;
            _slots = null;
            _layout = null;
            _timer?.Stop();
            _form?.HideMenu();
        }

        private void EnsureTimer()
        {
            if (_form == null || _form.IsDisposed)
                _form = new RadialMenuForm();
            if (_timer != null)
                return;
            _timer = new Timer { Interval = WatchdogIntervalMs };
            _timer.Tick += (_, __) => Tick();
        }

        private void Tick()
        {
            if (!IsActive)
            {
                _timer.Stop();
                return;
            }

            if ((DateTime.UtcNow - _beganUtc).TotalMilliseconds > MaximumHoldMs)
            {
                Cancel("Timeout");
                return;
            }

            // A protected window can swallow the button-up hook. Our own hook
            // blocks the button-down, so the asynchronous key state normally
            // never reports the button as pressed: only treat "up" as a real
            // release after the key state has first been seen down.
            var down = (GetAsyncKeyState(VK_MBUTTON) & 0x8000) != 0;
            if (down)
                _observedDown = true;
            var released = _observedDown && !down;
            if (released && (DateTime.UtcNow - _lastHookUtc).TotalMilliseconds > 300)
            {
                if (++_releasedTicks >= 3)
                {
                    Logging.LogMessage("Radial menu release recovered from key state.");
                    End(Cursor.Position);
                    return;
                }
            }
            else
            {
                _releasedTicks = 0;
            }

            if (_open && _dirty)
            {
                _dirty = false;
                _form.Render(_layout, _cursor, _selected);
            }
        }

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
    }
}
