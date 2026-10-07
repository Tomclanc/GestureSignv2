using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Common.Input
{
    // Track movement within each gesture, including fingers returning to their start.
    public sealed class TouchScreenReleaseTracker
    {
        private readonly Dictionary<int, Point> _starts = new Dictionary<int, Point>();
        private readonly Dictionary<int, Point> _positions = new Dictionary<int, Point>();
        private readonly HashSet<int> _moving = new HashSet<int>();
        private const int AnchorTolerance = 20; // Physical screen pixels.
        public bool WaitingForContact { get; private set; }
        public bool AllReleased => _positions.Count == 0;

        // A returning moving finger may reuse its HID ID. Only a fresh contact,
        // never an update from an existing anchor, can re-arm recognition.
        public bool Observe(int id, Point position, bool down)
        {
            var newContact = WaitingForContact && down && !_positions.ContainsKey(id);
            if (down)
            {
                if (!_starts.TryGetValue(id, out var start))
                    _starts[id] = start = position;
                var dx = (long)position.X - start.X;
                var dy = (long)position.Y - start.Y;
                if (dx * dx + dy * dy > AnchorTolerance * AnchorTolerance) _moving.Add(id);
                _positions[id] = position;
            }
            else _positions.Remove(id);
            return newContact;
        }

        // Called after every contact in the HID frame has been merged, so
        // anchor coordinates and contact order are independent of report order.
        public bool TryRearm(bool newContact)
        {
            if (!WaitingForContact || !newContact || AllReleased) return false;
            _starts.Clear();
            _moving.Clear();
            foreach (var contact in _positions) _starts[contact.Key] = contact.Value;
            WaitingForContact = false;
            return true;
        }

        public bool ShouldComplete(IEnumerable<int> released) =>
            !WaitingForContact &&
            (AllReleased || released.Any(_moving.Contains) && _positions.Keys.All(id => !_moving.Contains(id)));

        public void Complete()
        {
            WaitingForContact = !AllReleased;
            if (!WaitingForContact) Reset();
        }

        public void Reset()
        {
            _starts.Clear(); _moving.Clear(); _positions.Clear(); WaitingForContact = false;
        }
    }
}