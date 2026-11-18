using System;
using System.Drawing;
using System.Linq;
namespace GestureSign.PointPatterns
{
    public static class TemplateTurnEvidence
    {
        // A strong two-finger curved drawing can outweigh a low intent-model score.
        // Similarity alone is insufficient: both fingers must make the expected turn,
        // and the chosen template must clearly beat the next different gesture.
        public static bool StrongMatch(Point[][] captured, Point[][] template, double score, double? otherScore)
        {
            if (captured == null || template == null || captured.Length != 2 || template.Length != 2 ||
                !double.IsFinite(score) || score < 85 ||
                otherScore.HasValue && (!double.IsFinite(otherScore.Value) || score - otherScore.Value < 10))
                return false;
            return Enumerable.Range(0, 2).All(i =>
            {
                double expected = Turn(template[i]), actual = Turn(captured[i]);
                return expected >= 45 && actual >= 40 && Math.Abs(expected - actual) <= 25;
            });
        }

        // Compare sustained directions over arc length, not adjacent noisy points.
        public static double Turn(Point[] points)
        {
            if (points == null || points.Length < 3) return 0;
            var distance = new double[points.Length];
            for (int i = 1; i < points.Length; i++) distance[i] = distance[i-1] + Math.Sqrt(Math.Pow((double)points[i].X-points[i-1].X,2)+Math.Pow((double)points[i].Y-points[i-1].Y,2));
            double length = distance[distance.Length-1];
            if (length < 4) return 0;
            PointF At(double fraction)
            {
                double target = length * fraction;
                int i = 1; while (i < points.Length-1 && distance[i] < target) i++;
                double t = (target-distance[i-1])/Math.Max(.000001,distance[i]-distance[i-1]);
                return new PointF((float)(points[i-1].X+t*(points[i].X-points[i-1].X)),(float)(points[i-1].Y+t*(points[i].Y-points[i-1].Y)));
            }
            var a=At(.05); var b=At(.30); var c=At(.70); var d=At(.95);
            double ax=b.X-a.X, ay=b.Y-a.Y, bx=d.X-c.X, by=d.Y-c.Y;
            double norm=Math.Sqrt((ax*ax+ay*ay)*(bx*bx+by*by));
            return norm < .001 ? 0 : Math.Acos(Math.Clamp((ax*bx+ay*by)/norm,-1,1))*180/Math.PI;
        }
        public static bool MissingTurn(Point[][] captured, Point[][] template)
        {
            if (captured == null || template == null || captured.Length is < 1 or > 4 || template.Length != captured.Length) return false;
            // Require agreement from every captured finger and every corresponding template.
            return Enumerable.Range(0,captured.Length).All(i => Turn(template[i]) >= 55 && Turn(template[i]) <= 125 && Turn(captured[i]) < 25);
        }
    }
}
