using System;
using System.Collections.Generic;
using System.Linq;
namespace GestureSign.PieMenu;

internal static class PieLayout
{
    internal static double Normalize(double angle) => (angle % 360 + 360) % 360;
    internal static double Distance(double a, double b) => Math.Abs(Normalize(a - b + 180) - 180);

    // Port of computeItemAngles in Kando src/common/math/index.ts.
    // Copyright Simon Schneegans. MIT license, see EditorAssets/MIT.txt.
    internal static double[] Arrange(IReadOnlyList<PieItem> items, double? parentAngle = null)
    {
        var angles = new double[items.Count];
        if (items.Count == 0) return angles;
        var anchors = new List<(int Index, double Angle)>();
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].Angle is not { } degrees || !double.IsFinite(degrees) || degrees < 0) continue;
            var angle = Normalize(degrees);
            if (parentAngle is { } parent && Distance(angle, parent) < .0001) angle += .1;
            if (anchors.Count == 0 || angle >= anchors[^1].Angle) anchors.Add((i, angle));
        }
        if (anchors.Count == 0)
        {
            var start = parentAngle is { } parent
                ? Enumerable.Range(1, items.Count).Min(i => Normalize(parent + i * 360d / (items.Count + 1))) : 0;
            anchors.Add((0, start));
        }
        for (var anchor = 0; anchor < anchors.Count; anchor++)
        {
            var (firstIndex, firstAngle) = anchors[anchor];
            var (lastIndex, lastAngle) = anchors[(anchor + 1) % anchors.Count];
            angles[firstIndex] = Normalize(firstAngle);
            if (lastAngle <= firstAngle) lastAngle += 360;
            var count = (lastIndex - firstIndex - 1 + items.Count) % items.Count;
            var parent = parentAngle is { } value ? firstAngle + Normalize(value - firstAngle) : double.NaN;
            var reserve = parent > firstAngle && parent < lastAngle;
            var step = (lastAngle - firstAngle) / (count + (reserve ? 2 : 1));
            var position = 1;
            for (var i = (firstIndex + 1) % items.Count; i != lastIndex; i = (i + 1) % items.Count)
            {
                var angle = firstAngle + step * position;
                if (reserve && angle + step / 2 > parent)
                {
                    angle += step;
                    position++;
                    reserve = false;
                }
                angles[i] = Normalize(angle);
                position++;
            }
        }
        return angles;
    }

    internal static int Hit(double x, double y, IReadOnlyList<double> angles, double deadZone)
    {
        if (x * x + y * y < deadZone * deadZone) return -1;
        var direction = Normalize(Math.Atan2(x, -y) * 180 / Math.PI);
        var result = -1;
        var nearest = 361.0;
        for (var i = 0; i < angles.Count; i++)
        {
            var distance = Distance(direction, angles[i]);
            if (distance >= nearest) continue;
            nearest = distance;
            result = i;
        }
        return result;
    }
}
