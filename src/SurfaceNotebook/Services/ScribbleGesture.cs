using System.Windows;
using System.Windows.Ink;

namespace SurfaceNotebook.Services;

// Runs after pen-up, without changing the live renderer. Require substantial
// reversals in a small region; ordinary writing and diagrams stay ordinary ink.
public static class ScribbleGesture
{
    public static bool IsScribble(Stroke stroke) => Analyze(stroke) is not null;

    public sealed class Trace(IReadOnlyList<Point> path, IReadOnlyList<IReadOnlyList<Point>> passes)
    {
        public IReadOnlyList<Point> Path { get; } = path;
        public bool CrossesRepeatedly(Rect target)
        {
            if (target.IsEmpty) return false;
            target.Inflate(8, 8);
            var hits = 0;
            foreach (var pass in passes)
            {
                for (var i = 1; i < pass.Count; i++)
                    if (Intersects(pass[i - 1], pass[i], target))
                    {
                        if (++hits >= 2) return true;
                        break;
                    }
            }
            return false;
        }
    }

    public static Trace? Analyze(Stroke stroke)
    {
        if (stroke.StylusPoints.Count < 4 || stroke.StylusPoints.Count > 50000) return null;
        var points = Resample(stroke);
        if (points.Count < 4) return null;
        var center = new Point(points.Average(p => p.X), points.Average(p => p.Y));
        double xx = 0, xy = 0, yy = 0, length = 0;
        foreach (var point in points)
        {
            var offset = point - center;
            xx += offset.X * offset.X; xy += offset.X * offset.Y; yy += offset.Y * offset.Y;
        }
        // Use the gesture's own axis, independent of page orientation or pen sample rate.
        var angle = .5 * Math.Atan2(2 * xy, xx - yy);
        var axis = new Vector(Math.Cos(angle), Math.Sin(angle));
        var projected = points.Select(p => Vector.Multiply(p - center, axis)).ToArray();
        var perpendicular = points.Select(p => Vector.Multiply(p - center, new Vector(-axis.Y, axis.X))).ToArray();
        var span = projected.Max() - projected.Min();
        var crossSpan = perpendicular.Max() - perpendicular.Min();
        double travel = 0;
        for (var i = 1; i < points.Count; i++)
        { length += (points[i] - points[i - 1]).Length; travel += Math.Abs(projected[i] - projected[i - 1]); }
        if (span < 12 || span > 500 || crossSpan > 180 || crossSpan > span * .8 ||
            length < span * 2.1 || travel < length * .7) return null;
        var threshold = Math.Max(2, span * .12);
        var extreme = projected[0]; var extremeIndex = 0;
        var direction = 0;
        var turns = new List<int>(); var bends = new List<int>();
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
                var local = Turn(points, extremeIndex, Math.Min(6, span * .1));
                var rounded = Turn(points, extremeIndex, Math.Clamp(span * .22, 8, 24));
                if (local.Dot < .25 || (crossSpan <= span * .5 && rounded.Dot < .5))
                {
                    turns.Add(extremeIndex);
                    if (Math.Abs(rounded.Cross) > .01) bends.Add(Math.Sign(rounded.Cross));
                }
                direction = -direction; extreme = value; extremeIndex = i;
            }
        }
        if (turns.Count < 2) return null;
        // Repeated circles/ovals keep bending the same way. A scratch alternates
        // its bends, or retraces a straight path with no lateral bend at all.
        if (bends.Count >= 2 && bends.All(b => b == bends[0])) return null;
        var ends = new[] { 0 }.Concat(turns).Append(points.Count - 1).ToArray();
        var passes = new List<IReadOnlyList<Point>>();
        for (var i = 1; i < ends.Length; i++) passes.Add(points.GetRange(ends[i - 1], ends[i] - ends[i - 1] + 1));
        return new Trace(points, passes);
    }
    private static (double Dot, double Cross) Turn(IReadOnlyList<Point> points, int index, double window)
    {
        var before = index; var after = index; double distance = 0;
        while (before > 0 && distance < window) { distance += (points[before] - points[before - 1]).Length; before--; }
        distance = 0;
        while (after < points.Count - 1 && distance < window) { distance += (points[after + 1] - points[after]).Length; after++; }
        var incoming = points[index] - points[before]; var outgoing = points[after] - points[index];
        var denominator = incoming.Length * outgoing.Length;
        if (denominator < 1) return (1, 0);
        return (Vector.Multiply(incoming, outgoing) / denominator, Vector.CrossProduct(incoming, outgoing) / denominator);
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

    public static bool IsLocalTarget(Stroke gesture, Stroke target)
    {
        var scratch = gesture.GetBounds(); var bounds = target.GetBounds();
        // A narrow vertical scratch can cover only a small fraction of a connected
        // word. Accept that overlap, but protect long diagram lines crossing it.
        return Math.Max(bounds.Width, bounds.Height) <= Math.Max(240, Math.Max(scratch.Width, scratch.Height) * 3);
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
