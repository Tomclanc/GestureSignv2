using System;
using System.Drawing;
using System.Linq;
namespace GestureSign.PointPatterns
{
    public static class TemplateTurnEvidence
    {
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
            if (captured == null || template == null || captured.Length != 2 || template.Length != 2) return false;
            // Require agreement from both fingers and a strong turn in both templates.
            return Enumerable.Range(0,2).All(i => Turn(template[i]) >= 55 && Turn(template[i]) <= 125 && Turn(captured[i]) < 25);
        }
    }
}
