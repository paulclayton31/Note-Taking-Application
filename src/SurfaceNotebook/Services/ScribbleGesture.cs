using System.Windows;
using System.Windows.Ink;

namespace SurfaceNotebook.Services;

// Runs after pen-up, without changing the live renderer. Require substantial
// reversals in a small region; ordinary writing and diagrams stay ordinary ink.
public static class ScribbleGesture
{
    public static bool IsScribble(Stroke stroke)
    {
        var points = stroke.StylusPoints;
        if (points.Count < 7 || points.Count > 50000) return false;
        var bounds = new Rect(points[0].ToPoint(), points[0].ToPoint());
        double length = 0, horizontal = 0, vertical = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var delta = points[i].ToPoint() - points[i - 1].ToPoint();
            bounds.Union(points[i].ToPoint());
            length += delta.Length; horizontal += Math.Abs(delta.X); vertical += Math.Abs(delta.Y);
        }
        var major = Math.Max(bounds.Width, bounds.Height); var minor = Math.Min(bounds.Width, bounds.Height);
        if (major < 24 || major > 320 || minor < 8 || minor > 140 || minor > major * .75 || length < major * 5) return false;
        var useX = bounds.Width >= bounds.Height;
        if ((useX ? horizontal : vertical) < length * .8) return false;
        var threshold = Math.Max(6, major * .3);
        double extreme = useX ? points[0].X : points[0].Y;
        var direction = 0; var reversals = 0;
        foreach (var point in points)
        {
            var value = useX ? point.X : point.Y;
            if (direction == 0)
            {
                if (Math.Abs(value - extreme) >= threshold) { direction = Math.Sign(value - extreme); extreme = value; }
            }
            else if ((value - extreme) * direction > 0) extreme = value;
            else if ((extreme - value) * direction >= threshold) { reversals++; direction = -direction; extreme = value; }
        }
        return reversals >= 5;
    }

    public static bool CrossesRepeatedly(IReadOnlyList<Point> path, Rect target)
    {
        if (target.IsEmpty || path.Count < 2) return false;
        target.Inflate(2, 2);
        var runs = 0; var previousHit = false; var allInside = target.Contains(path[0]);
        for (var i = 1; i < path.Count; i++)
        {
            var hit = Intersects(path[i - 1], path[i], target);
            if (hit && (!previousHit || !target.Contains(path[i - 1]))) runs++;
            if (runs >= 3) return true;
            previousHit = hit; allInside &= target.Contains(path[i]);
        }
        return allInside;
    }
    private static bool Intersects(Point a, Point b, Rect rectangle)
    {
        double low = 0, high = 1;
        var delta = b - a;
        bool Clip(double p, double q)
        {
            if (Math.Abs(p) < .000001) return q >= 0;
            var ratio = q / p;
            if (p < 0) low = Math.Max(low, ratio); else high = Math.Min(high, ratio);
            return low <= high;
        }
        return Clip(-delta.X, a.X - rectangle.Left) && Clip(delta.X, rectangle.Right - a.X) &&
            Clip(-delta.Y, a.Y - rectangle.Top) && Clip(delta.Y, rectangle.Bottom - a.Y);
    }
}
