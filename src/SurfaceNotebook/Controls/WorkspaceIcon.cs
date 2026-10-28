using System.Windows;
using System.Windows.Media;

namespace SurfaceNotebook.Controls;

public sealed class WorkspaceIcon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(string), typeof(WorkspaceIcon),
        new FrameworkPropertyMetadata("Pen", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(WorkspaceIcon),
        new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public string Kind { get => (string)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public WorkspaceIcon() { Width = 20; Height = 20; IsHitTestVisible = false; }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var path = Kind switch
        {
            "Home" => "M3,11 L12,3 21,11 M5,10 L5,21 10,21 10,15 14,15 14,21 19,21 19,10",
            "Folder" => "M3,6 L9,6 11,8 21,8 21,21 3,21 Z M3,6 L3,4 9,4 11,6 20,6 20,8",
            "FolderPlus" => "M3,6 L9,6 11,8 21,8 21,21 3,21 Z M12,11 L12,18 M8.5,14.5 L15.5,14.5",
            "Clock" => "M21,12 A9,9 0 1 1 3,12 A9,9 0 1 1 21,12 M12,6 L12,12 16,15",
            "Pen" or "FountainPen" => "M5,19 L8,10 15,3 21,9 14,16 Z M5,19 L11,13 M10,8 L16,14 M4,21 L10,21",
            "BallPen" => "M5,19 L7,14 17,4 Q18,3 19,4 L21,6 Q22,7 21,8 L11,18 Z M7,14 L11,18 M16,5 L20,9",
            "Paste" => "M8,5 L5,5 5,22 20,22 20,5 17,5 M9,3 L16,3 16,7 9,7 Z M9,12 L16,12 M9,16 L14,16",
            "Highlight" => "M7,14 L15,3 21,8 12,18 Z M7,14 L4,19 9,21 12,18 M3,22 L18,22",
            "Eraser" => "M3,15 L13,3 Q14,2 15,3 L21,8 Q22,9 21,10 L12,21 8,21 Z M8,10 L17,15 M12,21 L22,21",
            "Lasso" => "M8,19 C-1,17 1,5 11,4 C22,2 25,15 16,18 C10,21 4,17 8,14 C13,10 14,23 9,23",
            "Shape" => "M3,4 L15,4 15,16 3,16 Z M15,11 C25,9 25,23 17,22 C11,22 9,18 11,16",
            "Text" => "M4,5 L20,5 M12,5 L12,21 M8,21 L16,21",
            "Calculate" => "M5,2 L19,2 19,22 5,22 Z M8,6 L16,6 M8,11 L10,11 M14,11 L16,11 M8,15 L10,15 M14,15 L16,15 M8,19 L10,19 M15,18 L15,20",
            "Hand" => "M7,12 L7,6 Q7,3 9,4 L10,11 10,3 Q12,1 13,4 L13,11 14,4 Q16,3 17,6 L17,12 18,8 Q21,7 21,11 L20,18 Q18,23 12,22 L8,20 3,14 Q3,11 6,13 L9,15",
            "Undo" => "M9,5 L3,10 9,15 M3,10 L15,10 C23,10 23,21 15,21",
            "Redo" => "M15,5 L21,10 15,15 M21,10 L9,10 C1,10 1,21 9,21",
            "Sidebar" => "M4,3 L20,3 Q22,3 22,5 L22,19 Q22,21 20,21 L4,21 Q2,21 2,19 L2,5 Q2,3 4,3 M8,3 L8,21",
            "Notebook" => "M5,3 L20,3 20,21 5,21 Q3,21 3,19 L3,5 Q3,3 5,3 M7,3 L7,21 M10,8 L17,8",
            "Page" => "M5,2 L14,2 20,8 20,22 5,22 Z M14,2 L14,8 20,8 M9,13 L16,13 M9,17 L16,17",
            "Plus" => "M12,4 L12,20 M4,12 L20,12",
            "Close" => "M6,6 L18,18 M6,18 L18,6",
            "Chevron" => "M9,5 L16,12 9,19",
            "Down" => "M5,9 L12,16 19,9",
            "Search" => "M17,10 A7,7 0 1 1 3,10 A7,7 0 1 1 17,10 M15,15 L22,22",
            "Star" => "M12,2 L15,9 22,10 17,15 18,22 12,18 6,22 7,15 2,10 9,9 Z",
            "Focus" => "M3,9 L3,3 9,3 M15,3 L21,3 21,9 M21,15 L21,21 15,21 M9,21 L3,21 3,15",
            "Moon" => "M20,15 C13,20 3,13 9,3 C-1,7 1,22 12,22 C16,22 20,19 20,15",
            "Sun" => "M17,12 A5,5 0 1 1 7,12 A5,5 0 1 1 17,12 M12,1 L12,3 M12,21 L12,23 M1,12 L3,12 M21,12 L23,12 M4,4 L6,6 M18,18 L20,20 M4,20 L6,18 M18,6 L20,4",
            "Paper" => "M5,2 L19,2 19,22 5,22 Z M8,8 L16,8 M8,12 L16,12 M8,16 L16,16",
            "More" => "M4,12 L5,12 M11,12 L12,12 M18,12 L19,12",
            "Fit" => "M3,8 L3,3 8,3 M16,3 L21,3 21,8 M21,16 L21,21 16,21 M8,21 L3,21 3,16 M8,8 L16,8 16,16 8,16 Z",
            "Minus" => "M5,12 L19,12",
            "Check" => "M4,12 L9,17 20,6",
            "Import" => "M12,2 L12,15 M7,10 L12,15 17,10 M4,16 L4,22 20,22 20,16",
            "Export" => "M12,16 L12,3 M7,8 L12,3 17,8 M4,16 L4,22 20,22 20,16",
            "Grip" => "M8,5 L9,5 M15,5 L16,5 M8,12 L9,12 M15,12 L16,12 M8,19 L9,19 M15,19 L16,19",
            "Maximize" => "M5,5 L19,5 19,19 5,19 Z",
            "Settings" => "M4,6 L20,6 M4,12 L20,12 M4,18 L20,18 M8,3 L8,9 M16,9 L16,15 M10,15 L10,21",
            _ => "M4,12 L20,12"
        };
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var pen = new Pen(Foreground, 1.65) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, Geometry.Parse(path));
        dc.Pop();
    }
}
