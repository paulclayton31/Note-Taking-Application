using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;

namespace SurfaceNotebook.Services;

// Geometry only: a pause is required separately by HoldShapeGesture. Never run
// recognition on ordinary completed handwriting or on a scratch-out.
public static class HeldShapeRecognizer
{
    public sealed record Result(string Name, Stroke Stroke);

    public static Result? Recognize(Stroke input)
    {
        if (input.StylusPoints.Count is < 2 or > 20000 || ScribbleGesture.IsScribble(input)) return null;
        var points = new List<Point>();
        foreach (var p in input.StylusPoints.Select(p => p.ToPoint()))
            if (points.Count == 0 || (p - points[^1]).Length >= 1) points.Add(p);
        if (points.Count < 2) return null;
        if (points.Count > 5000) return null;
        var first = points[0]; var last = points[^1];
        var distance = (last - first).Length;
        var length = points.Zip(points.Skip(1), (a, b) => (b - a).Length).Sum();
        if (distance >= 24 && length <= distance * 1.18 && points.All(p => SegmentDistance(p, first, last) <= Math.Max(2.5, distance * .045)))
            return Make("Line", [first, last], input);
        var bounds = Rect.Empty; foreach (var p in points) bounds.Union(p);
        var diagonal = Math.Sqrt(bounds.Width * bounds.Width + bounds.Height * bounds.Height);
        if (bounds.Width < 16 || bounds.Height < 16 || distance > Math.Max(10, diagonal * .18) || length < diagonal * 1.8) return null;

        // Start at an extremity and simplify both halves of the closed outline.
        // This works even when the pen started midway along a rectangle's edge.
        var center = new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
        var start = Enumerable.Range(0, points.Count).MaxBy(i => (points[i] - center).LengthSquared);
        points = points.Skip(start).Concat(points.Take(start)).ToList(); points.Add(points[0]);
        var opposite = Enumerable.Range(1, points.Count - 2).MaxBy(i => (points[i] - points[0]).LengthSquared);
        var vertices = Simplify(points.Take(opposite + 1).ToList(), diagonal * .045);
        vertices.AddRange(Simplify(points.Skip(opposite).ToList(), diagonal * .045).Skip(1));
        vertices.RemoveAt(vertices.Count - 1);
        if (vertices.Count == 3)
        {
            var area = Math.Abs(Vector.CrossProduct(vertices[1] - vertices[0], vertices[2] - vertices[0])) / 2;
            if (area > bounds.Width * bounds.Height * .2 && OutlineError(points, vertices) < diagonal * .045)
                return Make("Triangle", vertices.Append(vertices[0]), input);
        }
        if (vertices.Count == 4 && OutlineError(points, vertices) < diagonal * .045)
        {
            var edges = Enumerable.Range(0, 4).Select(i => vertices[(i + 1) % 4] - vertices[i]).ToArray();
            if (edges.All(e => e.Length >= 12) && Enumerable.Range(0, 4).All(i =>
                Math.Abs(Vector.Multiply(edges[i], edges[(i + 1) % 4])) / (edges[i].Length * edges[(i + 1) % 4].Length) < .28))
            {
                var u = edges[0] - edges[2]; u.Normalize(); var v = new Vector(-u.Y, u.X);
                var c = new Point(vertices.Average(p => p.X), vertices.Average(p => p.Y));
                var width = (edges[0].Length + edges[2].Length) / 2;
                var height = (edges[1].Length + edges[3].Length) / 2;
                var square = Math.Abs(width - height) / Math.Max(width, height) < .16;
                if (square) width = height = (width + height) / 2;
                Point Corner(int x, int y) => c + u * (x * width / 2) + v * (y * height / 2);
                return Make(square ? "Square" : "Rectangle", [Corner(-1, -1), Corner(1, -1), Corner(1, 1), Corner(-1, 1), Corner(-1, -1)], input);
            }
        }
        // Normalize an ellipse in its own orientation, then check radial error
        // and a single revolution. Repeated loops and open arcs stay freehand.
        var mean = new Point(points.Average(p => p.X), points.Average(p => p.Y));
        var xx = points.Sum(p => (p.X - mean.X) * (p.X - mean.X));
        var yy = points.Sum(p => (p.Y - mean.Y) * (p.Y - mean.Y));
        var xy = points.Sum(p => (p.X - mean.X) * (p.Y - mean.Y));
        var angle = .5 * Math.Atan2(2 * xy, xx - yy);
        var axis = new Vector(Math.Cos(angle), Math.Sin(angle)); var normal = new Vector(-axis.Y, axis.X);
        var local = points.Select(p => new Point(Vector.Multiply(p - center, axis), Vector.Multiply(p - center, normal))).ToArray();
        var minX = local.Min(p => p.X); var maxX = local.Max(p => p.X); var minY = local.Min(p => p.Y); var maxY = local.Max(p => p.Y);
        var rx = (maxX - minX) / 2; var ry = (maxY - minY) / 2;
        if (Math.Min(rx, ry) < 8) return null;
        var cx = (maxX + minX) / 2; var cy = (maxY + minY) / 2;
        var normalized = local.Select(p => new Vector((p.X - cx) / rx, (p.Y - cy) / ry)).ToArray();
        if (normalized.Average(p => Math.Abs(p.Length - 1)) > .09 || normalized.Any(p => Math.Abs(p.Length - 1) > .25)) return null;
        double sweep = 0, travel = 0;
        for (var i = 1; i < normalized.Length; i++)
        {
            var turn = Math.Atan2(Vector.CrossProduct(normalized[i - 1], normalized[i]), Vector.Multiply(normalized[i - 1], normalized[i]));
            sweep += turn; travel += Math.Abs(turn);
        }
        if (Math.Abs(sweep) < 5.4 || travel > 7.5) return null;
        var circle = Math.Abs(rx - ry) / Math.Max(rx, ry) < .16;
        if (circle) rx = ry = (rx + ry) / 2;
        var origin = center + axis * cx + normal * cy;
        return Make(circle ? "Circle" : "Ellipse", Enumerable.Range(0, 97).Select(i => origin + axis * (rx * Math.Cos(i * Math.PI / 48)) + normal * (ry * Math.Sin(i * Math.PI / 48))), input);
    }
    private static Result Make(string name, IEnumerable<Point> points, Stroke input)
    {
        var attributes = input.DrawingAttributes.Clone(); attributes.FitToCurve = false; attributes.IgnorePressure = true;
        return new Result(name, new Stroke(new StylusPointCollection(points.Select(p => new StylusPoint(p.X, p.Y, .5f))), attributes));
    }
    private static double OutlineError(List<Point> points, List<Point> vertices) => points.Max(p => Enumerable.Range(0, vertices.Count)
        .Min(i => SegmentDistance(p, vertices[i], vertices[(i + 1) % vertices.Count])));
    private static double SegmentDistance(Point p, Point a, Point b)
    {
        var edge = b - a;
        var t = edge.LengthSquared == 0 ? 0 : Math.Clamp(Vector.Multiply(p - a, edge) / edge.LengthSquared, 0, 1);
        return (p - (a + edge * t)).Length;
    }
    private static List<Point> Simplify(List<Point> points, double tolerance)
    {
        var keep = new bool[points.Count]; keep[0] = keep[^1] = true;
        var pending = new Stack<(int Start, int End)>(); pending.Push((0, points.Count - 1));
        while (pending.TryPop(out var segment))
        {
            var furthest = -1; var distance = tolerance;
            for (var i = segment.Start + 1; i < segment.End; i++)
            {
                var next = SegmentDistance(points[i], points[segment.Start], points[segment.End]);
                if (next > distance) { distance = next; furthest = i; }
            }
            if (furthest < 0) continue;
            keep[furthest] = true; pending.Push((segment.Start, furthest)); pending.Push((furthest, segment.End));
        }
        return points.Where((_, i) => keep[i]).ToList();
    }
}
