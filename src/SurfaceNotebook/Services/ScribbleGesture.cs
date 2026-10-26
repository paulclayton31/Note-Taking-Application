using System.Windows;
using System.Windows.Ink;

namespace SurfaceNotebook.Services;

// Runs after pen-up, without changing the live renderer. Require substantial
// reversals in a small region; ordinary writing and diagrams stay ordinary ink.
public static class ScribbleGesture
{
    public static bool IsScribble(Stroke stroke)
    {
        if (stroke.StylusPoints.Count < 4 || stroke.StylusPoints.Count > 50000) return false;
        var points = Resample(stroke);
        if (points.Count < 4) return false;
        var center = new Point(points.Average(p => p.X), points.Average(p => p.Y));
        double xx = 0, xy = 0, yy = 0, length = 0;
        foreach (var point in points)
        {
            var offset = point - center;
            xx += offset.X * offset.X; xy += offset.X * offset.Y; yy += offset.Y * offset.Y;
        }
        // Work in the gesture's own axis, so a diagonal scratch is as easy as a horizontal one.
        var angle = .5 * Math.Atan2(2 * xy, xx - yy);
        var axis = new Vector(Math.Cos(angle), Math.Sin(angle));
        var projected = points.Select(p => Vector.Multiply(p - center, axis)).ToArray();
        var perpendicular = points.Select(p => Vector.Multiply(p - center, new Vector(-axis.Y, axis.X))).ToArray();
        var span = projected.Max() - projected.Min();
        var crossSpan = perpendicular.Max() - perpendicular.Min();
        double travel = 0;
        for (var i = 1; i < points.Count; i++)
        { length += (points[i] - points[i - 1]).Length; travel += Math.Abs(projected[i] - projected[i - 1]); }
        if (span < 16 || span > 500 || crossSpan > 180 || crossSpan > span * .8 ||
            length < span * 2.55 || travel < length * .7) return false;
        var threshold = Math.Max(3, span * .18);
        var extreme = projected[0]; var extremeIndex = 0;
        var direction = 0; var reversals = 0;
        for (var i = 1; i < points.Count; i++)
        {
            var value = projected[i];
            if (direction == 0)
            {
                if (Math.Abs(value - extreme) >= threshold) { direction = Math.Sign(value - extreme); extreme = value; extremeIndex = i; }
            }
            else if ((value - extreme) * direction > 0) { extreme = value; extremeIndex = i; }
            else if ((extreme - value) * direction >= threshold)
            {
                if (SharpTurn(points, extremeIndex, Math.Min(6, span * .1))) reversals++;
                direction = -direction; extreme = value; extremeIndex = i;
            }
        }
        return reversals >= 2;
    }
    private static bool SharpTurn(IReadOnlyList<Point> points, int index, double window)
    {
        var before = index; var after = index; double distance = 0;
        while (before > 0 && distance < window) { distance += (points[before] - points[before - 1]).Length; before--; }
        distance = 0;
        while (after < points.Count - 1 && distance < window) { distance += (points[after + 1] - points[after]).Length; after++; }
        var incoming = points[index] - points[before]; var outgoing = points[after] - points[index];
        if (incoming.Length < 1 || outgoing.Length < 1) return false;
        return Vector.Multiply(incoming, outgoing) / (incoming.Length * outgoing.Length) < .25;
    }
    private static List<Point> Resample(Stroke stroke)
    {
        const double step = 2;
        var points = new List<Point> { stroke.StylusPoints[0].ToPoint() };
        var previous = points[0]; double remaining = step;
        foreach (var point in stroke.StylusPoints.Skip(1).Select(p => p.ToPoint()))
        {
            var delta = point - previous;
            while (delta.Length >= remaining && points.Count < 6000)
            {
                previous += delta * (remaining / delta.Length); points.Add(previous);
                delta = point - previous; remaining = step;
            }
            if (points.Count >= 6000) return [];
            remaining -= delta.Length; previous = point;
        }
        if ((points[^1] - previous).Length > .1) points.Add(previous);
        return points;
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
            if (runs >= 2) return true;
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
