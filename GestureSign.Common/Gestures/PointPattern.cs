using GestureSign.PointPatterns;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace GestureSign.Common.Gestures
{
    public class PointPattern : IPointPattern
    {
        public PointPattern(Point[][] points)
        {
            Points = points;
        }

        public PointPattern(IEnumerable<List<Point>> points)
        {
            Points = points.Select(l => l.ToArray()).ToArray();
        }

        public Point[][] Points { get; set; }

        // Older files omit this field and continue honoring the capture preference.
        public bool OrderByStartPosition { get; set; }

        public Point[][] GetComparisonPoints(bool orderByLocation)
            => ForComparison(Points, orderByLocation || OrderByStartPosition);

        public static Point[][] ForComparison(Point[][] points, bool orderByStartPosition)
            => !orderByStartPosition || points == null ? points : GestureSign.Shared.GestureStrokeOrder.ByStart(
                points, p => p.Length == 0 ? double.PositiveInfinity : p[0].X,
                p => p.Length == 0 ? double.PositiveInfinity : p[0].Y);
    }
}
