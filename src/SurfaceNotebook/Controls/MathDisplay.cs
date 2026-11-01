using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath.Exceptions;

namespace SurfaceNotebook.Controls;

public sealed class MathDisplay : ContentControl
{
    public static readonly DependencyProperty FormulaProperty = DependencyProperty.Register(nameof(Formula), typeof(string), typeof(MathDisplay), new PropertyMetadata("", Changed));
    public string Formula { get => (string)GetValue(FormulaProperty); set => SetValue(FormulaProperty, value); }
    static MathDisplay() => ForegroundProperty.OverrideMetadata(typeof(MathDisplay), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.Inherits, Changed));
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((MathDisplay)d).Rebuild();
    private void Rebuild()
    {
        var text=Formula;
        if (string.IsNullOrWhiteSpace(text)) { Content=null; return; }
        try
        {
            var geometry=WpfTeXFormulaParser.Instance.Parse(text).RenderToGeometry(WpfTeXEnvironment.Create(scale:20), scale:20);
            Content=new ScrollViewer { Content=new MathGlyphSurface(geometry, Foreground), HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, CanContentScroll=false };
        }
        catch (Exception ex) when (ex is TexException or ArgumentException or InvalidOperationException)
        { Content=new TextBox { Text=text, IsReadOnly=true, TextWrapping=TextWrapping.Wrap, Foreground=Foreground, Background=Brushes.Transparent, BorderThickness=new Thickness(0) }; }
    }
}

// Formula glyphs can extend above/below (or left of) their baseline. Normalize
// the *actual* geometry bounds before measuring so ScrollViewer cannot crop them.
public sealed class MathGlyphSurface : FrameworkElement
{
    private readonly Geometry geometry;
    private readonly Brush brush;
    public Rect GlyphBounds { get; }
    public MathGlyphSurface(Geometry geometry, Brush brush)
    { this.geometry=geometry; this.brush=brush; GlyphBounds=geometry.Bounds; }
    protected override Size MeasureOverride(Size availableSize) => GlyphBounds.IsEmpty ? new Size() : new Size(Math.Ceiling(GlyphBounds.Width)+16, Math.Ceiling(GlyphBounds.Height)+16);
    protected override void OnRender(DrawingContext dc)
    {
        if (GlyphBounds.IsEmpty) return;
        dc.PushTransform(new TranslateTransform(8-GlyphBounds.X,8-GlyphBounds.Y));
        dc.DrawGeometry(brush,null,geometry); dc.Pop();
    }
}
