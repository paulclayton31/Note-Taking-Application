using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SurfaceNotebook.Controls;

// A native, resolution-independent input surface over a shared HSV disk.
// Brightness is independent so black, white, and muted ink remain easy to choose.
public sealed class ColorWheel : FrameworkElement
{
    public static readonly DependencyProperty SelectedColorProperty = DependencyProperty.Register(nameof(SelectedColor),
        typeof(Color), typeof(ColorWheel), new FrameworkPropertyMetadata(Colors.Black,
            FrameworkPropertyMetadataOptions.AffectsRender, (d, e) => ((ColorWheel)d).ColorChanged((Color)e.NewValue)));
    private static readonly BitmapSource Palette = CreatePalette();
    private double hue, saturation, brightness;
    private bool picking;
    private int? touch;
    public event Action<Color>? SelectedColorChanged;
    public Color SelectedColor { get => (Color)GetValue(SelectedColorProperty); set => SetValue(SelectedColorProperty, value); }
    public double Brightness
    {
        get => brightness;
        set { if (!double.IsFinite(value)) return; brightness = Math.Clamp(value, 0, 1); Commit(); }
    }
    public ColorWheel()
    {
        Focusable = true;
        System.Windows.Automation.AutomationProperties.SetName(this, "Color wheel. Left and right change hue; up and down change saturation.");
    }
    private void ColorChanged(Color color)
    {
        if (!picking) (hue, saturation, brightness) = ToHsv(color);
        SelectedColorChanged?.Invoke(color);
    }
    private void Commit()
    {
        picking = true;
        try { SelectedColor = FromHsv(hue, saturation, brightness, SelectedColor.A); }
        finally { picking = false; }
        InvalidateVisual();
    }
    public void ChooseAt(Point point)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size < 16) return;
        var radius = size / 2 - 10;
        var delta = point - new Point(ActualWidth / 2, ActualHeight / 2);
        hue = (Math.Atan2(-delta.Y, delta.X) * 180 / Math.PI + 360) % 360;
        saturation = Math.Clamp(delta.Length / radius, 0, 1);
        Commit();
    }
    protected override Size MeasureOverride(Size availableSize) => new(224, 224);
    protected override void OnRender(DrawingContext dc)
    {
        var radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 10);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawImage(Palette, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        var angle = hue * Math.PI / 180;
        var marker = center + new Vector(Math.Cos(angle), -Math.Sin(angle)) * (radius * saturation);
        dc.DrawEllipse(new SolidColorBrush(SelectedColor), new Pen(Brushes.Black, 3), marker, 7, 7);
        dc.DrawEllipse(null, new Pen(Brushes.White, 2), marker, 7, 7);
        if (IsKeyboardFocused) dc.DrawEllipse(null, new Pen(SystemColors.HighlightBrush, 2), center, radius + 6, radius + 6);
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    { Focus(); CaptureMouse(); ChooseAt(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e)
    { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) { ChooseAt(e.GetPosition(this)); e.Handled = true; } }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    { if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnTouchDown(TouchEventArgs e)
    {
        e.Handled = true;
        if (touch is not null) return;
        touch = e.TouchDevice.Id; if (e.TouchDevice.IsActive) e.TouchDevice.Capture(this); Focus(); ChooseAt(e.GetTouchPoint(this).Position);
    }
    protected override void OnTouchMove(TouchEventArgs e)
    { e.Handled = true; if (touch == e.TouchDevice.Id) ChooseAt(e.GetTouchPoint(this).Position); }
    protected override void OnTouchUp(TouchEventArgs e)
    { e.Handled = true; if (touch == e.TouchDevice.Id) { touch = null; if (e.TouchDevice.IsActive) e.TouchDevice.Capture(null); } }
    protected override void OnLostTouchCapture(TouchEventArgs e)
    { if (touch == e.TouchDevice.Id) touch = null; base.OnLostTouchCapture(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Left: hue = (hue + 355) % 360; break;
            case Key.Right: hue = (hue + 5) % 360; break;
            case Key.Up: saturation = Math.Min(1, saturation + .05); break;
            case Key.Down: saturation = Math.Max(0, saturation - .05); break;
            default: base.OnKeyDown(e); return;
        }
        Commit(); e.Handled = true;
    }
    public static (double Hue, double Saturation, double Value) ToHsv(Color color)
    {
        var r = color.R / 255d; var g = color.G / 255d; var b = color.B / 255d;
        var max = Math.Max(r, Math.Max(g, b)); var min = Math.Min(r, Math.Min(g, b)); var delta = max - min;
        var hue = delta == 0 ? 0 : max == r ? 60 * ((g - b) / delta % 6) : max == g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
        return ((hue + 360) % 360, max == 0 ? 0 : delta / max, max);
    }
    public static Color FromHsv(double hue, double saturation, double value, byte alpha = 255)
    {
        hue = (hue % 360 + 360) % 360; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        var chroma = value * saturation; var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1)); var m = value - chroma;
        var (r, g, b) = hue switch
        { < 60 => (chroma, x, 0d), < 120 => (x, chroma, 0d), < 180 => (0d, chroma, x), < 240 => (0d, x, chroma), < 300 => (x, 0d, chroma), _ => (chroma, 0d, x) };
        return Color.FromArgb(alpha, (byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    private static BitmapSource CreatePalette()
    {
        const int size = 224; var pixels = new byte[size * size * 4];
        for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
        {
            var dx = (x - (size - 1) / 2d) / (size / 2d); var dy = ((size - 1) / 2d - y) / (size / 2d);
            var radius = Math.Sqrt(dx * dx + dy * dy); if (radius > 1) continue;
            var color = FromHsv(Math.Atan2(dy, dx) * 180 / Math.PI, radius, 1); var i = (y * size + x) * 4;
            pixels[i] = color.B; pixels[i + 1] = color.G; pixels[i + 2] = color.R; pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze(); return bitmap;
    }
}
