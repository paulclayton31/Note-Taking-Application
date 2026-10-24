using System.Windows;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Controls;

// A single retained drawing, shared by the page, thumbnails, and settings preview.
// No visual effects or bitmap cache are placed above the real-time ink renderer.
public sealed class PaperSurface : FrameworkElement
{
    public static readonly DependencyProperty PaperProperty = DependencyProperty.Register(nameof(Paper),
        typeof(PaperSettings), typeof(PaperSurface), new FrameworkPropertyMetadata(PaperSettings.Default, FrameworkPropertyMetadataOptions.AffectsRender));
    public PaperSettings Paper { get => (PaperSettings)GetValue(PaperProperty); set => SetValue(PaperProperty, value); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        Draw(dc, Paper, new Size(ActualWidth, ActualHeight));
    }
    public static void Draw(DrawingContext dc, PaperSettings paper, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(size)));
        dc.DrawRectangle(Brush(paper.Background), null, new Rect(size));
        dc.PushTransform(new ScaleTransform(size.Width / paper.Width, size.Height / paper.Height));
        var lines = Brush(paper.LineColor);
        var pen = new Pen(lines, 0.7); pen.Freeze();
        var spacing = paper.LineSpacing;
        var w = paper.Width; var h = paper.Height;
        switch (paper.Pattern)
        {
            case PaperPattern.College:
            case PaperPattern.Wide:
                for (var y = 80.0; y < h - 24; y += spacing)
                    dc.DrawLine(pen, new Point(32, y), new Point(w - 32, y));
                break;
            case PaperPattern.Graph:
            case PaperPattern.Engineering:
                for (var x = spacing; x < w; x += spacing) dc.DrawLine(pen, new Point(x, 0), new Point(x, h));
                for (var y = spacing; y < h; y += spacing) dc.DrawLine(pen, new Point(0, y), new Point(w, y));
                if (paper.Pattern == PaperPattern.Engineering)
                {
                    var major = new Pen(lines, 1.5);
                    for (var x = spacing * 5; x < w; x += spacing * 5) dc.DrawLine(major, new Point(x, 0), new Point(x, h));
                    for (var y = spacing * 5; y < h; y += spacing * 5) dc.DrawLine(major, new Point(0, y), new Point(w, y));
                }
                break;
            case PaperPattern.Dots:
                for (var x = spacing; x < w; x += spacing)
                    for (var y = spacing; y < h; y += spacing) dc.DrawEllipse(lines, null, new Point(x, y), 1, 1);
                break;
        }
        dc.Pop(); dc.Pop();
    }
    public static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze(); return brush;
    }
}
