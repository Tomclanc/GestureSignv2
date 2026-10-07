using System;
using System.Collections.Generic;
using System.Linq;

namespace GestureSign.Shared
{
    internal static class GestureStrokeOrder
    {
        // Match spatial roles by the initial position, even when fingers cross.
        // OrderBy is stable for coincident starts; never reorder the capture itself.
        internal static T[] ByStart<T>(IEnumerable<T> strokes, Func<T, double> startX, Func<T, double> startY)
            => strokes.OrderBy(startX).ThenBy(startY).ToArray();
    }
}
