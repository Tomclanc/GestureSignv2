using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Common.Input
{
    // Track lifetime movement, including fingers that return to their start.
    public sealed class TouchScreenReleaseTracker
    {
        private readonly Dictionary<int, Point> _starts = new Dictionary<int, Point>();
        private readonly HashSet<int> _moving = new HashSet<int>();
        private readonly HashSet<int> _active = new HashSet<int>();
        private const int AnchorTolerance = 20; // Physical screen pixels.
        public bool Draining { get; private set; }
        public bool AllReleased => _active.Count == 0;
        public void Observe(int id, Point position, bool down)
        {
            if (down)
            {
                if (!_starts.TryGetValue(id, out var start))
                    _starts[id] = start = position;
                var dx = (long)position.X - start.X;
                var dy = (long)position.Y - start.Y;
                if (dx * dx + dy * dy > AnchorTolerance * AnchorTolerance) _moving.Add(id);
                _active.Add(id);
            }
            else _active.Remove(id);
        }
        public bool ShouldComplete(IEnumerable<int> released) =>
            AllReleased || released.Any(_moving.Contains) && _active.All(id => !_moving.Contains(id));
        public void Complete()
        {
            Draining = !AllReleased;
            if (!Draining) Reset();
        }
        public void Reset()
        {
            _starts.Clear(); _moving.Clear(); _active.Clear(); Draining = false;
        }
    }
}