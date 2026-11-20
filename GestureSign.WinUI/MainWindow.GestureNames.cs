using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace GestureSign.WinUI;

public sealed partial class MainWindow
{
    // Display aliases only: action bindings and saved correction targets keep their IDs.
    private static readonly HashSet<string> DefaultGestureNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "三指左滑", "三指L形", "S形手势", "双指左滑", "双指上滑", "双指左下滑", "双指上下滑",
        "双指平行左滑", "双指平行上滑", "双指L形", "双指右上滑", "三指右滑", "双指下滑",
        "三指下滑", "双指点按下滑", "双指点按右滑", "双指上划", "三指双击", "双指弧线右滑",
        "双指前进", "五指点按", "四指L形", "三指点按", "双指点按", "四指点按", "五指下滑",
        "三指上滑", "四指下滑", "四指右滑", "四指左滑"
    };
    private string? DefaultGestureCaption(string name)
    {
        if (!DefaultGestureNames.Contains(name)) return null;
        var gesture = _legacyData?.Gestures.FirstOrDefault(g => g.Name == name);
        return gesture is null ? null : DescribeGesture(gesture);
    }

    private string GestureDisplayName(LegacyGesture gesture)
    {
        if (DefaultGestureNames.Contains(gesture.Name) || IsLegacyGeneratedGestureName(gesture.Name))
            return DescribeGesture(gesture) ?? DisplayName(gesture.Name);
        return DisplayName(gesture.Name);
    }

    private static bool IsLegacyGeneratedGestureName(string name)
        => name.Length == 6 &&
           name.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9') &&
           name.Any(char.IsAsciiDigit) && name.Any(char.IsAsciiLetter);

    private string? DescribeGesture(LegacyGesture gesture)
    {
        if (gesture.PointPatterns.Count == 0) return null;
        var paths = gesture.PointPatterns;
        double Extent(IReadOnlyList<(double X, double Y)> p) => p.Count == 0 ? 0 : Math.Max(p.Max(v => v.X) - p.Min(v => v.X), p.Max(v => v.Y) - p.Min(v => v.Y));
        double size = paths.Max(Extent);
        int stages = (gesture.Source["PointPatterns"] as JsonArray)?.Count ?? 1;
        if (size < 4)
            return IntentFormat(stages == 2 ? "{0}-finger double tap" : "{0}-finger tap", gesture.FingerCount);
        var moving = paths.Where(p => Extent(p) >= Math.Max(4, size * .08)).ToArray();
        int fixedCount = paths.Count - moving.Length;
        var directions = moving.Select(p => GesturePathDirections(p, size * .12)).ToArray();
        bool held = fixedCount == 1 && moving.Length == 1;
        string Caption(string suffix)
        {
            if (held)
                return IntentFormat("Hold one finger; other finger " + suffix.Replace("{1}", "{0}"), directions[0]);
            var movement = IntentFormat("{0}-finger " + suffix, moving.Length, directions[0]);
            return WithHeldFingers(movement);
        }
        string WithHeldFingers(string movement) => fixedCount == 0 ? movement
            : F("{0}指固定，{1}", "Hold {0} finger(s) · {1}", fixedCount, movement);

        if (directions.Distinct().Count() != 1) return Caption("combined paths");
        string dir = directions[0];
        var swipe = dir switch { "←" => "swipe left", "→" => "swipe right", "↑" => "swipe up", "↓" => "swipe down", _ => null };
        if (swipe is not null) return Caption(swipe);
        if (dir is "↘" or "↗" or "↙" or "↖")
            return WithHeldFingers(F("{0}指斜滑（{1}）", "{0}-finger diagonal swipe ({1})", moving.Length, dir));
        if (gesture.Name == "S形手势" && dir.Length > 2) return Caption("draw S");
        if (dir.Length == 2 && dir[0] != dir[1]) return Caption("turn ({1})");
        return Caption("curved path");
    }
    private static string GesturePathDirections(IReadOnlyList<(double X, double Y)> points, double tolerance)
    {
        var simplified = new List<(double X, double Y)>();
        void Simplify(int start, int end)
        {
            var a = points[start]; var b = points[end]; double dx = b.X-a.X, dy = b.Y-a.Y, len = dx*dx+dy*dy;
            double best = 0; int split = -1;
            for (int i = start + 1; i < end; i++)
            {
                double t = len == 0 ? 0 : Math.Clamp(((points[i].X-a.X)*dx+(points[i].Y-a.Y)*dy)/len, 0, 1);
                double distance = Math.Sqrt(Math.Pow(points[i].X-a.X-t*dx,2)+Math.Pow(points[i].Y-a.Y-t*dy,2));
                if (distance > best) { best = distance; split = i; }
            }
            if (split >= 0 && best > tolerance) { Simplify(start, split); Simplify(split, end); }
            else simplified.Add(a);
        }
        Simplify(0, points.Count - 1); simplified.Add(points[^1]);
        string result = "";
        for (int i = 1; i < simplified.Count; i++)
        {
            double dx = simplified[i].X-simplified[i-1].X, dy = simplified[i].Y-simplified[i-1].Y;
            if (Math.Sqrt(dx*dx+dy*dy) < tolerance) continue;
            var x = Math.Abs(dx);
            var y = Math.Abs(dy);
            string next = Math.Min(x, y) >= Math.Max(x, y) * .6
                ? (dx > 0 ? (dy > 0 ? "↘" : "↗") : (dy > 0 ? "↙" : "↖"))
                : x > y ? (dx > 0 ? "→" : "←") : (dy > 0 ? "↓" : "↑");
            if (!result.EndsWith(next, StringComparison.Ordinal)) result += next;
        }
        return result;
    }
}
