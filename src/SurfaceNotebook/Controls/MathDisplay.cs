using System.Windows;
using System.Windows.Controls;
using WpfMath.Controls;

namespace SurfaceNotebook.Controls;

// FormulaControl handles a bounded LaTeX subset locally, with no browser or remote assets.
public sealed class MathDisplay : ContentControl
{
    public static readonly DependencyProperty FormulaProperty = DependencyProperty.Register(nameof(Formula), typeof(string), typeof(MathDisplay), new PropertyMetadata("", Changed));
    public string Formula { get => (string)GetValue(FormulaProperty); set => SetValue(FormulaProperty, value); }
    private static void Changed(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var display=(MathDisplay)d; var text=(string?)e.NewValue ?? "";
        if (text.Length==0) { display.Content=null; return; }
        var formula=new FormulaControl { Formula=text, Scale=20, HorizontalAlignment=HorizontalAlignment.Left };
        formula.SetBinding(ForegroundProperty,new System.Windows.Data.Binding(nameof(Foreground)) { Source=display });
        if (formula.HasError) display.Content=new TextBox { Text=text, IsReadOnly=true, TextWrapping=TextWrapping.Wrap, BorderThickness=new Thickness(0) };
        else display.Content=new ScrollViewer { Content=formula, HorizontalScrollBarVisibility=ScrollBarVisibility.Auto, VerticalScrollBarVisibility=ScrollBarVisibility.Disabled, Padding=new Thickness(4,8,4,8) };
    }
}
