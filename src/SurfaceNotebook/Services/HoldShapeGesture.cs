using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;

namespace SurfaceNotebook.Services;

public sealed class HoldShapeGesture
{
    public const int HoldMilliseconds = 600;
    private readonly List<StylusPoint> points = [];
    private DrawingAttributes? attributes;
    private Point anchor;
    private long stationarySince;
    private bool checkedThisPause;
    public HeldShapeRecognizer.Result? Result { get; private set; }
    public bool Active => attributes is not null;
    public void Begin(DrawingAttributes drawingAttributes, Point point, Point screenPoint, long now)
    {
        Cancel(); attributes = drawingAttributes.Clone(); anchor = screenPoint; stationarySince = now; checkedThisPause = false;
        points.Add(new StylusPoint(point.X, point.Y));
    }
    public void Move(Point point, Point screenPoint, long now)
    {
        if (!Active) return;
        if (points.Count >= 20000) { Cancel(); return; }
        points.Add(new StylusPoint(point.X, point.Y));
        if ((screenPoint - anchor).Length > 4)
        { anchor = screenPoint; stationarySince = now; Result = null; checkedThisPause = false; }
    }
    public HeldShapeRecognizer.Result? Check(long now)
    {
        if (Active && !checkedThisPause && now - stationarySince >= HoldMilliseconds && points.Count >= 2)
        {
            checkedThisPause = true;
            Result = HeldShapeRecognizer.Recognize(new Stroke(new StylusPointCollection(points), attributes!));
        }
        return Result;
    }
    public void Cancel() { attributes = null; points.Clear(); Result = null; }
    public StylusPointCollection PreviewPoints() => new(points);
}
