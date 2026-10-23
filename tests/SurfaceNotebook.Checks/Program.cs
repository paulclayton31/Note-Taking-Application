using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            CheckRoundTripAndValidation();
            CheckHistory();
            CheckWindow(args.FirstOrDefault());
            Console.WriteLine("PASS: page round-trip, invalid input, history branching, WPF window and undo/redo.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static Stroke SampleStroke() => new(new StylusPointCollection
    {
        new StylusPoint(80, 96, 0.1f), new StylusPoint(120, 120, 0.8f), new StylusPoint(180, 96, 0.3f)
    }) { DrawingAttributes = new DrawingAttributes { Width = 3, Height = 3, Color = Colors.Blue, FitToCurve = true } };

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void CheckRoundTripAndValidation()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".stempage");
        try
        {
            var page = InkMapper.Capture(new StrokeCollection { SampleStroke() });
            PageStore.SaveAsync(path, page).GetAwaiter().GetResult();
            var loaded = PageStore.LoadAsync(path).GetAwaiter().GetResult();
            var restored = InkMapper.Capture(InkMapper.Restore(loaded));
            Require(restored.Strokes[0].Id == page.Strokes[0].Id, "Stroke identity lost.");
            Require(restored.Strokes[0].Points.SequenceEqual(page.Strokes[0].Points), "Coordinates or pressure lost.");
            Require(restored.Strokes[0].Color == page.Strokes[0].Color && restored.Strokes[0].Width == 3,
                "Drawing style lost.");
            var empty = page with { Strokes = [] };
            PageStore.SaveAsync(path, empty).GetAwaiter().GetResult();
            Require(PageStore.LoadAsync(path).GetAwaiter().GetResult().Strokes.Count == 0, "Overwrite failed.");
            try
            {
                PageStore.SaveAsync(path, page with { Version = 99 }).GetAwaiter().GetResult();
                throw new InvalidOperationException("Unsupported schema accepted.");
            }
            catch (InvalidDataException) { }
            Require(PageStore.LoadAsync(path).GetAwaiter().GetResult().Strokes.Count == 0, "Failed save damaged original.");
            File.WriteAllText(path, "{\"Version\":1,\"Width\":816,\"Height\":1056,\"Strokes\":[null]}");
            try
            {
                PageStore.LoadAsync(path).GetAwaiter().GetResult();
                throw new InvalidOperationException("Malformed stroke accepted.");
            }
            catch (InvalidDataException) { }
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    private static void CheckHistory()
    {
        var history = new InkHistory();
        var strokes = new StrokeCollection { SampleStroke() };
        history.Record(strokes);
        strokes.Clear();
        Require(history.Undo().Count == 0, "Undo failed.");
        Require(history.Redo().Count == 1, "History was mutated by live ink.");
        history.Undo();
        history.Record(new StrokeCollection { SampleStroke(), SampleStroke() });
        Require(!history.CanRedo && history.Undo().Count == 0, "New edit did not replace redo branch.");
    }

    private static void CheckWindow(string? previewPath)
    {
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        var ink = (PenCanvas)window.FindName("InkSurface");
        Require(!ink.AllowMouseDrawing && !ink.DefaultDrawingAttributes.IgnorePressure, "Input defaults incorrect.");
        ink.Strokes.Add(SampleStroke());
        var originalIdentity = InkMapper.Capture(ink.Strokes).Strokes[0].Id;
        ((RadioButton)window.FindName("EraserButton")).IsChecked = true;
        Require(ink.EditingMode == InkCanvasEditingMode.EraseByStroke, "Eraser control failed.");
        ((RadioButton)window.FindName("PenButton")).IsChecked = true;
        Require(ink.EditingMode == InkCanvasEditingMode.Ink, "Pen control failed.");
        ((ComboBox)window.FindName("ColorPicker")).SelectedIndex = 2;
        Require(ink.DefaultDrawingAttributes.Color.R == 206 && ink.Strokes[0].DrawingAttributes.Color == Colors.Blue,
            "Color changes must only affect new ink.");
        ((Button)window.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Strokes.Count == 0, "Window undo failed.");
        ((Button)window.FindName("RedoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Strokes.Count == 1, "Window redo failed.");
        Require(InkMapper.Capture(ink.Strokes).Strokes[0].Id == originalIdentity, "Redo lost stroke identity.");
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1180, 900));
        content.Arrange(new Rect(0, 0, 1180, 900));
        content.UpdateLayout();
        if (previewPath is not null)
        {
            var bitmap = new RenderTargetBitmap(1180, 900, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(previewPath);
            encoder.Save(stream);
        }
        // The window is never shown; hardware pen behavior needs a real Surface session.
    }
}
