using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static Stroke Outline(params Point[] corners)
    {
        var points = new List<StylusPoint>();
        for (var i = 1; i < corners.Length; i++)
            for (var j = 0; j < 20; j++)
            {
                var p = corners[i - 1] + (corners[i] - corners[i - 1]) * (j / 20d);
                points.Add(new StylusPoint(p.X + .65 * Math.Sin(j), p.Y + .65 * Math.Cos(j), .2f + .6f * j / 20));
            }
        points.Add(new StylusPoint(corners[^1].X, corners[^1].Y, .3f));
        return new Stroke(new StylusPointCollection(points)) { DrawingAttributes = new DrawingAttributes { Width = 3.5, Height = 3.5, Color = Colors.Coral } };
    }
    private static HoldShapeGesture StartHold(Stroke stroke)
    {
        var gesture = new HoldShapeGesture(); var first = stroke.StylusPoints[0].ToPoint();
        gesture.Begin(stroke.DrawingAttributes, first, first, 0);
        for (var i = 1; i < stroke.StylusPoints.Count; i++)
        {
            var p = stroke.StylusPoints[i].ToPoint(); gesture.Move(p, p, i * 5);
        }
        return gesture;
    }
    private static void CheckPenGestures(string root)
    {
        var line = Outline(new Point(50, 70), new Point(220, 170));
        var triangle = Outline(new Point(100, 60), new Point(180, 200), new Point(30, 200), new Point(100, 60));
        var square = Outline(new Point(20, 30), new Point(120, 30), new Point(120, 130), new Point(20, 130), new Point(20, 30));
        var rectangle = Outline(new Point(80, 30), new Point(220, 30), new Point(220, 110), new Point(20, 110), new Point(20, 30), new Point(80, 30));
        Stroke Ellipse(double rx, double ry, double turns = 1) => new(new StylusPointCollection(Enumerable.Range(0, 161)
            .Select(i => new StylusPoint(150 + rx * Math.Cos(i * Math.PI * 2 * turns / 160), 160 + ry * Math.Sin(i * Math.PI * 2 * turns / 160)))));
        var circle = Ellipse(55, 54); var ellipse = Ellipse(85, 40);
        foreach (var (stroke, name) in new[] { (line, "Line"), (triangle, "Triangle"), (square, "Square"), (rectangle, "Rectangle"), (circle, "Circle"), (ellipse, "Ellipse") })
            foreach (var angle in new[] { 0d, .4, 1.2 })
            {
                var rotated = stroke.Clone(); rotated.Transform(new Matrix(Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), 300, 200), false);
                var result = HeldShapeRecognizer.Recognize(rotated);
                Require(result?.Name == name, $"Held {name} at angle {angle} recognized as {result?.Name ?? "nothing"}.");
                Require(result!.Stroke.DrawingAttributes.Color == stroke.DrawingAttributes.Color && result.Stroke.DrawingAttributes.Width == stroke.DrawingAttributes.Width &&
                    result.Stroke.DrawingAttributes.IgnorePressure && !result.Stroke.DrawingAttributes.FitToCurve, "Snapping lost the pen style or left curved geometry.");
                if (name != "Line") Require((result.Stroke.StylusPoints[0].ToPoint() - result.Stroke.StylusPoints[^1].ToPoint()).Length < .01, "A snapped outline did not close.");
            }
        foreach (var negative in new[] { SampleStroke(), Scribble(0, 0), Ellipse(55, 35, 2), Ellipse(55, 35, .7),
            Outline(new Point(0, 0), new Point(4, 4)), Outline(new Point(20, 20), new Point(60, 120), new Point(90, 20), new Point(130, 120), new Point(160, 20)) })
            Require(HeldShapeRecognizer.Recognize(negative) is null, "Handwriting, an open arc, repeated loop, scratch, or tiny mark snapped into a shape.");
        var held = StartHold(line);
        Require(held.Check(500) is null, "A moving or briefly paused stroke snapped early.");
        Require(held.Check(10000)?.Name == "Line", "An intentional hold did not snap.");
        var endpoint = line.StylusPoints[^1].ToPoint();
        held.Move(endpoint + new Vector(1, 1), endpoint + new Vector(1, 1), 10010);
        Require(held.Result is not null, "Small pen jitter canceled the shape preview.");
        held.Move(endpoint + new Vector(30, 25), endpoint + new Vector(30, 25), 10020);
        Require(held.Result is null && held.Check(10100) is null, "Continuing the drawing did not cancel the preview and restart the hold.");
        held.Cancel(); Require(held.Check(20000) is null && !held.Active, "Canceled input still created a shape.");
        // Screen-space tolerance must remain stable while zoomed in or out.
        held.Begin(line.DrawingAttributes, new Point(0, 0), new Point(0, 0), 0);
        held.Move(new Point(100, 0), new Point(20, 0), 100);
        held.Move(new Point(110, 0), new Point(22, 0), 500);
        Require(held.Check(701) is not null, "Small screen-space jitter reset a zoomed-out hold.");

        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        var originalPage = window.CurrentPageId;
        Guid snappedPage;
        try
        {
            var ink = (PenCanvas)window.FindName("InkSurface");
            var raw = line.Clone();
            var controller = (HoldShapeGesture)typeof(MainWindow).GetField("heldShape", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            controller.Begin(raw.DrawingAttributes, raw.StylusPoints[0].ToPoint(), new Point(0, 0), 0);
            foreach (var p in raw.StylusPoints.Skip(1)) controller.Move(p.ToPoint(), p.ToPoint(), 100);
            Require(controller.Check(1000) is not null, "Integration hold did not recognize a line.");
            Collect(ink, raw);
            Require(ink.Strokes.Count == 1 && ink.Strokes[0].StylusPoints.Count == 2, "A held pen stroke did not replace freehand ink.");
            Click((Button)window.FindName("UndoButton"));
            Require(ink.Strokes.Single().StylusPoints.Count == raw.StylusPoints.Count, "Undo did not restore the original freehand stroke.");
            Click((Button)window.FindName("RedoButton")); Require(ink.Strokes.Single().StylusPoints.Count == 2, "Shape snapping did not redo.");
            Click((Button)window.FindName("UndoButton")); Click((Button)window.FindName("UndoButton"));
            Collect(ink, raw.Clone()); Require(ink.Strokes.Single().StylusPoints.Count == raw.StylusPoints.Count, "A stroke without a hold was automatically converted.");
            foreach (var tool in new[] { "Pen", "Highlighter", "Eraser", "Hand", "Shape", "Text", "Lasso" })
            {
                ((RadioButton)window.FindName(tool + "Button")).IsChecked = true;
                var originalMode = ink.EditingMode; var attrs = ink.DefaultDrawingAttributes.Clone();
                window.SetBarrelSelection(true);
                Require(ink.EditingMode == InkCanvasEditingMode.Select && !((PageViewport)window.FindName("Viewport")).HandMode, "Side-button lasso did not override the current tool or hand navigation.");
                Require(((RadioButton)window.FindName(tool + "Button")).IsChecked == true, "Side-button lasso changed the chosen toolbar tool.");
                ink.Select(new StrokeCollection { ink.Strokes[0] });
                Require(((Button)window.FindName("SelectionActionsButton")).Visibility == Visibility.Visible, "Side-button selection had no copy/duplicate/delete actions.");
                window.SetBarrelSelection(false);
                Require(ink.EditingMode == originalMode && ink.DefaultDrawingAttributes.Color == attrs.Color && ink.DefaultDrawingAttributes.Width == attrs.Width &&
                    ((PageViewport)window.FindName("Viewport")).HandMode == (tool == "Hand"), "Side-button release did not restore the prior tool and pen settings.");
            }
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            window.SetBarrelSelection(true); window.ShowHome(); window.ShowWorkspace();
            Require(ink.EditingMode == InkCanvasEditingMode.Ink, "Temporary lasso remained stuck after returning to the library.");
            window.SetBarrelSelection(true); window.ShowToolInspector();
            Require(ink.EditingMode == InkCanvasEditingMode.Ink, "Opening pen settings did not restore the chosen tool.");
            window.CloseToolInspector();
            snappedPage = Await(window.CreatePageAsync("Held shape"));
            controller.Begin(line.DrawingAttributes, line.StylusPoints[0].ToPoint(), new Point(0, 0), 0);
            foreach (var p in line.StylusPoints.Skip(1)) controller.Move(p.ToPoint(), p.ToPoint(), 100);
            controller.Check(1000); Collect(ink, line.Clone());
            ((CheckBox)window.FindName("HoldShapesToggle")).IsChecked = false;
            Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "Pen gesture changes did not save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        Require(reopened.CurrentPageId == snappedPage && ((PenCanvas)reopened.FindName("InkSurface")).Strokes.Single().StylusPoints.Count == 2 &&
            ((CheckBox)reopened.FindName("HoldShapesToggle")).IsChecked == false, "Restart lost the snapped shape or the draw-and-hold preference.");
        Await(reopened.ActivatePageAsync(originalPage!.Value));
        Require(((PenCanvas)reopened.FindName("InkSurface")).Strokes.Single().StylusPoints.Count == line.StylusPoints.Count, "Saving a shape changed freehand ink on another page.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Gesture test library did not close.");
    }
}
