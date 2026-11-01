using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static bool StrokeHasSelectionHighlight(Stroke stroke)
    {
        // The public selection can be empty while WPF still draws a cloned stroke
        // as selected. Inspect that separate flag to detect the original ghost.
        var property = typeof(Stroke).GetProperty("IsSelected", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("WPF's stroke selection flag was not found.");
        return (bool)property.GetValue(stroke)!;
    }

    private static void CheckSelectionHistory(string root)
    {
        var canvas = new InkCanvas { EditingMode = InkCanvasEditingMode.Select };
        var stroke = SampleStroke(); canvas.Strokes.Add(stroke);
        canvas.Select(new StrokeCollection { stroke });
        Require(StrokeHasSelectionHighlight(stroke), "The regression fixture did not select its stroke.");
        var customProperty = Guid.NewGuid(); stroke.AddPropertyData(customProperty, new int[] { 3, 5, 8 });
        stroke.DrawingAttributes.StylusTipTransform = new Matrix(1, .2, 0, 1, 0, 0);
        var history = new InkHistory(); history.Reset(canvas.Strokes);
        // Record while selected, then deselect, just like moving handwriting and
        // tapping blank paper before Undo. Test both Reset and Record snapshots.
        InkMapper.RenewIdentity(stroke); history.Record(canvas.Strokes);
        canvas.Select(new StrokeCollection(), Array.Empty<UIElement>());
        foreach (var restored in new[] { history.Undo(), history.Redo() })
        {
            var copy = restored.Single();
            Require(!StrokeHasSelectionHighlight(copy), "History restored WPF's hidden selection highlight.");
            Require(copy.StylusPoints.SequenceEqual(stroke.StylusPoints) &&
                copy.DrawingAttributes.StylusTipTransform == stroke.DrawingAttributes.StylusTipTransform &&
                ((int[])copy.GetPropertyData(customProperty)).SequenceEqual(new[] {3, 5, 8}),
                "Clearing history selection changed stroke content or metadata.");
        }
        Require(!StrokeHasSelectionHighlight(stroke), "Recording history changed the live stroke's deselection.");

        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        var ink = (PenCanvas)window.FindName("InkSurface");
        try
        {
            window.SaveCalculation("x = 2\nx + 3", new Point(100, 160));
            Collect(ink, SampleStroke());
            ((RadioButton)window.FindName("LassoButton")).IsChecked = true;
            var box = ink.Children.OfType<TextBox>().Single();
            box.SelectAll();
            ink.Select(new StrokeCollection { ink.Strokes.Single() }, [box]);
            window.DuplicateSelectedObjects();
            ink.Select(new StrokeCollection(), Array.Empty<UIElement>());
            void RequireDeselected()
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                Require(ink.GetSelectedStrokes().Count == 0 && ink.GetSelectedElements().Count == 0 && ink.GetSelectionBounds().IsEmpty,
                    "Undo/redo restored a lasso selection.");
                Require(ink.Strokes.All(s => !StrokeHasSelectionHighlight(s)) && ink.Children.OfType<TextBox>().All(b => b.SelectionLength == 0),
                    "Undo/redo left text or ink highlighted without a user selection.");
                Require(((Button)window.FindName("SelectionActionsButton")).Visibility == Visibility.Collapsed,
                    "Undo/redo reopened selection actions.");
            }
            for (var i = 0; i < 3; i++)
            {
                Click((Button)window.FindName("UndoButton")); RequireDeselected();
                Require(ink.Children.Count == 1 && ink.Strokes.Count == 1, "Undo changed the wrong content.");
                Click((Button)window.FindName("RedoButton")); RequireDeselected();
                Require(ink.Children.Count == 2 && ink.Strokes.Count == 2, "Redo failed to restore document content.");
            }
            // Undo also dismisses an active selection; the user can select again.
            ink.Select(new StrokeCollection { ink.Strokes[0] }, [ink.Children[0]]);
            Click((Button)window.FindName("UndoButton")); RequireDeselected();
            ink.Select(new StrokeCollection { ink.Strokes[0] }, [ink.Children[0]]);
            Require(ink.GetSelectedElements().Count == 1 && StrokeHasSelectionHighlight(ink.Strokes[0]),
                "The history fix prevented a new manual selection.");
            ink.Select(new StrokeCollection(), Array.Empty<UIElement>());
            Require(Await(window.FlushAsync()), "Selection history edits failed to save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
    }
}
