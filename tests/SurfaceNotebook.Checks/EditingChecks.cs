using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

internal static partial class Program
{
    private static Stroke Scribble(double left, double top, double width = 120, double height = 24) =>
        new(new StylusPointCollection(Enumerable.Range(0, 8).Select(i =>
            new StylusPoint(left + (i % 2 == 0 ? 0 : width), top + height * i / 7, .5f))));
    private static void Collect(PenCanvas ink, Stroke stroke)
    {
        ink.Strokes.Add(stroke);
        ink.RaiseEvent(new InkCanvasStrokeCollectedEventArgs(stroke) { RoutedEvent = InkCanvas.StrokeCollectedEvent });
    }
    private static void CheckEditingTools(string root)
    {
        var text = new PageText(Guid.NewGuid(), 80, 200, 440, 20, "#FF24324A", "Keep erase keep\nNext line");
        Guid page;
        using (var db = new NotebookDatabase(root))
        {
            page = db.Snapshot.LastPageId;
            db.ApplyDelta(page, [new StoredStroke(SampleData(), 0)], []);
            db.SaveTexts(page, [text]); db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace();
        var ink = (PenCanvas)window.FindName("InkSurface"); var viewport = (PageViewport)window.FindName("Viewport");
        var original = InkMapper.Capture(ink.Strokes).Strokes.Single();
        var shutdownMode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -30000; window.Top = -30000;
        window.Width = 1440; window.Height = 1000; window.Show(); ArrangeWorkspace(window, 1440, 1000);
        var clipboard = Clipboard.GetDataObject();
        try
        {
            Require(((ItemsControl)window.FindName("QuickPenList")).Items.Count == 3, "Saved pens are missing from the writing palette.");
            Require(((ItemsControl)window.FindName("ColorSwatches")).Items.Count == 18, "The color grid did not initialize.");
            var penButton = (RadioButton)window.FindName("PenButton");
            penButton.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent });
            penButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); ArrangeWorkspace(window, 1440, 1000);
            Require(window.IsToolInspectorOpen && ((FrameworkElement)window.FindName("ContextPanel")).ActualWidth == 252,
                "Tool settings did not open in a slim inspector.");
            Require(window.SetInkColor("#123abc") && ink.DefaultDrawingAttributes.Color == Color.FromRgb(0x12, 0x3a, 0xbc), "Custom pen color failed.");
            Require(!window.SetInkColor("oops") && ink.DefaultDrawingAttributes.Color == Color.FromRgb(0x12, 0x3a, 0xbc), "Invalid color changed the pen.");
            ((RadioButton)window.FindName("HighlighterButton")).IsChecked = true;
            Require(window.SetInkColor("#DDEE11"), "Custom highlighter color failed.");
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            Require(ink.DefaultDrawingAttributes.Color == Color.FromRgb(0x12, 0x3a, 0xbc), "Switching tools lost a custom pen color.");
            var preset = window.SavePenPreset("Custom study pen"); window.ApplyPenPreset(preset.Id);
            Require(((ItemsControl)window.FindName("QuickPenList")).Items.OfType<PresetItem>().Any(p => p.Id == preset.Id && p.Selected),
                "The active saved pen is not available on the palette.");
            Require(InkMapper.Capture(ink.Strokes).Strokes.Single() == original ||
                InkMapper.Capture(ink.Strokes).Strokes.Single().Points.SequenceEqual(original.Points), "Customizing tools changed existing handwriting.");
            Collect(ink, new Stroke(new StylusPointCollection { new StylusPoint(600, 800), new StylusPoint(620, 805) }));
            Require(!window.IsToolInspectorOpen, "Tool settings did not dismiss when writing started.");
            Click((Button)window.FindName("UndoButton"));

            foreach (var width in new[] { 640d, 960, 1440 })
            {
                window.Width = width; ArrangeWorkspace(window, width, 1000);
                var stage = (FrameworkElement)window.FindName("MainStage");
                var stageWidth = stage.ActualWidth;
                window.ShowToolInspector(); ArrangeWorkspace(window, width, 1000);
                if (width < 1280) Require(Math.Abs(stage.ActualWidth - stageWidth) < .1, "Compact tool settings narrowed the writing viewport.");
                var toolbar = (FrameworkElement)window.FindName("ToolbarFrame");
                Require(toolbar.ActualHeight <= 60 && toolbar.ActualWidth <= stage.ActualWidth - 23.9, "The writing palette wrapped or left the viewport.");
                RequireToolbarReachable(window);
                window.CloseToolInspector(); ArrangeWorkspace(window, width, 1000);
            }
            window.Width = 1440; ArrangeWorkspace(window, 1440, 1000);
            ((RadioButton)window.FindName("LassoButton")).IsChecked = true;
            ink.Select(new StrokeCollection { ink.Strokes.Single() }, ink.Children.OfType<TextBox>().Cast<UIElement>());
            ArrangeWorkspace(window, 1440, 1000);
            var actions = (Button)window.FindName("SelectionActionsButton");
            Require(actions.Visibility == Visibility.Visible, "Lasso selection actions did not appear.");
            var menu = actions.ContextMenu; menu.Opacity = 0;
            var selected = ink.GetSelectionBounds();
            var tap = ink.TranslatePoint(new Point(selected.Left + selected.Width / 2, selected.Top + selected.Height / 2), viewport);
            viewport.SetPenPresence(false);
            var view = viewport.View;
            viewport.BeginContact(201, tap); viewport.MoveContact(201, tap + new Vector(2, 1)); viewport.EndContact(201);
            Require(menu.IsOpen && viewport.View == view, "A finger tap did not open selection actions without moving the page.");
            menu.IsOpen = false;
            viewport.BeginContact(202, tap); viewport.MoveContact(202, tap + new Vector(30, 35)); viewport.EndContact(202);
            Require(!menu.IsOpen && viewport.View != view, "Dragging a selection with a finger did not remain page navigation.");
            viewport.SetView(view);
            viewport.BeginContact(203, tap); viewport.EndContact(203, canceled: true);
            Require(!menu.IsOpen, "Canceled touch capture opened selection actions.");
            viewport.BeginContact(204, tap); viewport.BeginContact(205, tap + new Vector(100, 0));
            viewport.EndContact(204); viewport.EndContact(205);
            Require(!menu.IsOpen, "A pinch opened selection actions.");
            viewport.SetPenPresence(true); viewport.BeginContact(206, tap); viewport.EndContact(206);
            Require(!menu.IsOpen, "Palm contact opened selection actions with the pen in range.");
            viewport.SetPenPresence(false);

            window.DuplicateSelectedObjects();
            Require(ink.Strokes.Count == 2 && ink.Children.Count == 2 && ink.GetSelectedStrokes().Count == 1 && ink.GetSelectedElements().Count == 1,
                "Duplicating a mixed selection lost content or its new selection.");
            Require(InkMapper.Identity(ink.Strokes[1]) != original.Id && ink.Children.OfType<TextBox>().Select(b => (Guid)b.Tag).Distinct().Count() == 2,
                "Duplicated objects reused their source identities.");
            Require(ink.Strokes[1].StylusPoints.Select(p => p.PressureFactor).SequenceEqual(original.Points.Select(p => p.Pressure)), "Duplication changed pressure.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Strokes.Count == 1 && ink.Children.Count == 1, "Duplicate did not undo as one edit.");
            Click((Button)window.FindName("RedoButton"));
            ink.Select(new StrokeCollection { ink.Strokes[1] }, [ink.Children.OfType<TextBox>().Last()]);
            window.DeleteSelectedObjects(); Require(ink.Strokes.Count == 1 && ink.Children.Count == 1, "Delete selection affected the source objects.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Strokes.Count == 2 && ink.Children.Count == 2, "Delete selection could not undo.");
            Click((Button)window.FindName("UndoButton"));
            ink.Select(new StrokeCollection { ink.Strokes.Single() }, ink.Children.OfType<TextBox>().Cast<UIElement>());
            window.CopySelectedObjects();
            var pastedPage = Await(window.CreatePageAsync("Pasted selection"));
            window.PasteSelectedObjects(new Point(100, 120));
            Require(ink.Strokes.Count == 1 && ink.Children.OfType<TextBox>().Single().Text == text.Text && InkMapper.Identity(ink.Strokes.Single()) != original.Id,
                "Copy/paste across pages lost content or reused identities.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Strokes.Count == 0 && ink.Children.Count == 0, "Paste was not one undoable edit.");
            Click((Button)window.FindName("RedoButton"));
            ((CheckBox)window.FindName("ScribbleEraseToggle")).IsChecked = false;
            Require(Await(window.FlushAsync()), "Editing changes did not flush.");
            Require(Await(window.ShutdownLibraryAsync()), "Editing library did not close cleanly.");
            var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
            Require(reopened.CurrentPageId == pastedPage && ((PenCanvas)reopened.FindName("InkSurface")).Strokes.Count == 1 &&
                ((CheckBox)reopened.FindName("ScribbleEraseToggle")).IsChecked == false && reopened.SavedPens.Any(p => p.Id == preset.Id),
                "Restart lost pasted ink, saved pens, or the scribble preference.");
            Await(reopened.ActivatePageAsync(page));
            Require(((PenCanvas)reopened.FindName("InkSurface")).Strokes.Single().StylusPoints.Select(p => p.PressureFactor).SequenceEqual(original.Points.Select(p => p.Pressure)),
                "Copying or editing the duplicate changed the original page.");
            Require(Await(reopened.ShutdownLibraryAsync()), "Reopened editing library did not close.");
        }
        finally
        {
            if (clipboard is null) Clipboard.Clear(); else Clipboard.SetDataObject(clipboard, true);
            Await(window.ShutdownLibraryAsync()); window.Close();
            Application.Current.ShutdownMode = shutdownMode;
        }
    }
    private static void CheckScribbleEditing(string root)
    {
        Require(ScribbleGesture.IsScribble(Scribble(70, 96)), "A deliberate back-and-forth scribble was not recognized.");
        var vertical = Scribble(70, 96); vertical.Transform(new Matrix(0, 1, 1, 0, 0, 0), false);
        Require(ScribbleGesture.IsScribble(vertical), "Vertical scribble recognition failed.");
        Require(!ScribbleGesture.IsScribble(SampleStroke()) && !ScribbleGesture.IsScribble(Scribble(20, 30, 8, 4)) &&
            !ScribbleGesture.IsScribble(Scribble(20, 30, 600, 150)), "Ordinary writing, tiny jitter, or a large diagram was recognized as erasing.");
        var wave = new Stroke(new StylusPointCollection(Enumerable.Range(0, 100).Select(i => new StylusPoint(i * 2, 80 + 12 * Math.Sin(i * .4)))));
        var circles = new Stroke(new StylusPointCollection(Enumerable.Range(0, 200).Select(i => new StylusPoint(100 + 35 * Math.Cos(i * .15), 100 + 20 * Math.Sin(i * .15)))));
        Require(!ScribbleGesture.IsScribble(wave) && !ScribbleGesture.IsScribble(circles), "A wave or circular doodle became an erase gesture.");
        var text = new PageText(Guid.NewGuid(), 80, 200, 440, 20, "#FF24324A", "Keep erase keep\nNext line");
        Guid first, second;
        using (var db = new NotebookDatabase(root))
        {
            first = db.Snapshot.LastPageId;
            var diagram = new Stroke(new StylusPointCollection { new StylusPoint(10, 110), new StylusPoint(700, 110) });
            var far = SampleStroke(); far.Transform(new Matrix(1, 0, 0, 1, 400, 400), false);
            db.ApplyDelta(first, InkMapper.Capture(new StrokeCollection { SampleStroke(), far, diagram }).Strokes.Select((s, i) => new StoredStroke(s, i)).ToArray(), []);
            second = Guid.NewGuid(); db.CreatePage(new PageSummary(second, db.Snapshot.Notebooks[0].Id, "Typed words", 1, null));
            db.SaveTexts(second, [text]); db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window, 1440, 1000);
        var ink = (PenCanvas)window.FindName("InkSurface");
        var originalIds = ink.Strokes.Select(InkMapper.Identity).ToArray();
        var mark = Scribble(70, 96, 120, 28); var markId = InkMapper.Identity(mark);
        Collect(ink, mark);
        Require(ink.Strokes.Count == 2 && ink.Strokes.All(s => InkMapper.Identity(s) != originalIds[0]), "Scribbling did not erase the covered handwriting.");
        Require(ink.Strokes.Select(InkMapper.Identity).SequenceEqual(originalIds.Skip(1)), "Scribbling erased distant ink or a long diagram crossing the region.");
        Click((Button)window.FindName("UndoButton"));
        Require(ink.Strokes.Count == 4 && ink.Strokes.Any(s => InkMapper.Identity(s) == markId) && ink.Strokes.Any(s => InkMapper.Identity(s) == originalIds[0]),
            "Undo did not restore both erased handwriting and the intentional scribble.");
        Click((Button)window.FindName("RedoButton")); Require(ink.Strokes.Count == 2, "Scribble erasing could not redo.");
        Click((Button)window.FindName("UndoButton")); Click((Button)window.FindName("UndoButton"));
        Require(ink.Strokes.Select(InkMapper.Identity).SequenceEqual(originalIds), "A second undo did not remove only the scribble.");
        ((CheckBox)window.FindName("ScribbleEraseToggle")).IsChecked = false;
        Collect(ink, Scribble(70, 96)); Require(ink.Strokes.Count == 4, "Disabled scribble erasing still deleted content.");
        Click((Button)window.FindName("UndoButton"));
        ((CheckBox)window.FindName("ScribbleEraseToggle")).IsChecked = true;
        ((RadioButton)window.FindName("HighlighterButton")).IsChecked = true;
        Collect(ink, Scribble(70, 96)); Require(ink.Strokes.Count == 4, "Highlighter marks triggered scribble erasing.");
        Click((Button)window.FindName("UndoButton"));
        ((RadioButton)window.FindName("PenButton")).IsChecked = true;
        Collect(ink, Scribble(300, 300)); Require(ink.Strokes.Count == 4, "A scribble on blank paper was discarded.");
        Click((Button)window.FindName("UndoButton"));
        Await(window.ActivatePageAsync(second)); ArrangeWorkspace(window, 1440, 1000);
        var box = ink.Children.OfType<TextBox>().Single();
        var start = box.GetRectFromCharacterIndex(5); var end = box.GetRectFromCharacterIndex(9, true);
        Require(!start.IsEmpty && !end.IsEmpty && end.X - start.X >= 24 && start.Height > 8, "Typed word geometry was not laid out for scribble testing.");
        var origin = box.TranslatePoint(start.TopLeft, ink);
        Collect(ink, Scribble(origin.X, origin.Y + 3, end.X - start.X, Math.Min(14, start.Height - 5)));
        Require(ink.Strokes.Count == 0 && ink.Children.OfType<TextBox>().Single().Text == "Keep keep\nNext line",
            "Scribbling a typed word did not preserve the other words and line break.");
        Click((Button)window.FindName("UndoButton"));
        Require(ink.Children.OfType<TextBox>().Single().Text == text.Text && ink.Strokes.Count == 1, "Typed word erasing did not undo with its mark.");
        Click((Button)window.FindName("RedoButton"));
        Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "Scribble edits did not save.");
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        Require(((PenCanvas)reopened.FindName("InkSurface")).Children.OfType<TextBox>().Single().Text == "Keep keep\nNext line", "Scribble edits did not survive restart.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Reopened scribble library did not close.");
    }
}
