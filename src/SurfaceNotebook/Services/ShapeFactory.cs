using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;

namespace SurfaceNotebook.Services;

public static class ShapeFactory
{
    public static StrokeCollection Create(Stroke gesture, int kind)
    {
        var first = gesture.StylusPoints[0].ToPoint(); var last = gesture.StylusPoints[^1].ToPoint();
        var bounds = new Rect(first, last);
        var attributes = gesture.DrawingAttributes.Clone(); attributes.IgnorePressure = true; attributes.FitToCurve = false;
        attributes.IsHighlighter = false;
        Stroke Make(IEnumerable<Point> points) => new(new StylusPointCollection(points.Select(p => new StylusPoint(p.X, p.Y, .5f))))
        { DrawingAttributes = attributes.Clone() };
        if ((last - first).Length < 3) return new StrokeCollection { Make([first, last]) };
        if (kind == 1)
            return new StrokeCollection { Make([bounds.TopLeft, bounds.TopRight, bounds.BottomRight, bounds.BottomLeft, bounds.TopLeft]) };
        if (kind == 2)
            return new StrokeCollection { Make(Enumerable.Range(0, 65).Select(i => new Point(bounds.X + bounds.Width / 2 * (1 + Math.Cos(i * Math.PI / 32)),
                bounds.Y + bounds.Height / 2 * (1 + Math.Sin(i * Math.PI / 32))))) };
        var result = new StrokeCollection { Make([first, last]) };
        if (kind == 3)
        {
            var direction = first - last; direction.Normalize();
            var head = Math.Min(18, (last - first).Length / 3);
            var normal = new Vector(-direction.Y, direction.X);
            result.Add(Make([last + direction * head + normal * head * .5, last, last + direction * head - normal * head * .5]));
        }
        return result;
    }
}
