using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public static class InkMapper
{
    private static readonly Guid IdProperty = new("61b0dd7d-4434-4e41-9ace-a7553ac4d137");

    public static Guid Identity(Stroke stroke)
    {
        if (!stroke.ContainsPropertyData(IdProperty)) stroke.AddPropertyData(IdProperty, Guid.NewGuid().ToString());
        return Guid.Parse((string)stroke.GetPropertyData(IdProperty));
    }

    public static void EnsureIdentities(StrokeCollection strokes)
    {
        foreach (var stroke in strokes)
            if (!stroke.ContainsPropertyData(IdProperty))
                stroke.AddPropertyData(IdProperty, Guid.NewGuid().ToString());
    }

    public static PageDocument Capture(StrokeCollection strokes) => new(
        PageDocument.CurrentVersion, PageDocument.DefaultWidth, PageDocument.DefaultHeight,
        strokes.Select(stroke =>
        {
            if (!stroke.ContainsPropertyData(IdProperty))
                stroke.AddPropertyData(IdProperty, Guid.NewGuid().ToString());
            var attributes = stroke.DrawingAttributes;
            return new InkStroke(Guid.Parse((string)stroke.GetPropertyData(IdProperty)),
                attributes.Color.ToString(), attributes.Width, attributes.FitToCurve,
                stroke.StylusPoints.Select(p => new InkPoint(p.X, p.Y, p.PressureFactor)).ToList());
        }).ToList());

    public static StrokeCollection Restore(PageDocument page)
    {
        page.Validate();
        var result = new StrokeCollection();
        foreach (var data in page.Strokes)
        {
            var stroke = new Stroke(new StylusPointCollection(data.Points.Select(p =>
                new StylusPoint(p.X, p.Y, p.Pressure))))
            {
                DrawingAttributes = new DrawingAttributes
                {
                    Color = (Color)ColorConverter.ConvertFromString(data.Color),
                    Width = data.Width, Height = data.Width,
                    FitToCurve = data.FitToCurve, IgnorePressure = false
                }
            };
            stroke.AddPropertyData(IdProperty, data.Id.ToString());
            result.Add(stroke);
        }
        return result;
    }
}
