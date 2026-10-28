using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckPaperAndMigration(string root)
    {
        foreach (var format in Enum.GetValues<PaperFormat>())
        {
            var portrait = new PaperSettings(Format: format);
            var landscape = portrait with { Landscape = true };
            portrait.Validate(); landscape.Validate();
            Require(portrait.Width == landscape.Height && portrait.Height == landscape.Width, "Orientation changed paper proportions.");
            foreach (var pattern in Enum.GetValues<PaperPattern>())
                (portrait with { Pattern = pattern }).Validate();
        }
        var a4 = new PaperSettings(Format: PaperFormat.A4, Pattern: PaperPattern.Engineering, Spacing: 16);
        Require(Math.Abs(a4.Width / a4.Height - 210.0 / 297) < .00001 && new PaperSettings(Format: PaperFormat.Legal).Height == 1344,
            "Physical paper dimensions are incorrect.");
        try { (a4 with { CustomWidth = double.NaN }).Validate(); throw new Exception("Invalid dimensions accepted."); }
        catch (InvalidDataException) { }
        var highlight = SampleStroke();
        highlight.DrawingAttributes.IsHighlighter = true; highlight.DrawingAttributes.Width = 28; highlight.DrawingAttributes.Height = 16;
        highlight.DrawingAttributes.StylusTip = StylusTip.Rectangle; highlight.DrawingAttributes.IgnorePressure = true;
        var restored = InkMapper.Restore(InkMapper.Capture(new StrokeCollection { highlight }))[0].DrawingAttributes;
        Require(restored.IsHighlighter && restored.Width == 28 && restored.Height == 16 && restored.IgnorePressure && restored.StylusTip == StylusTip.Rectangle,
            "Highlighter attributes did not round-trip.");
        var gesture = new Stroke(new StylusPointCollection { new StylusPoint(30, 40), new StylusPoint(130, 140) });
        for (var kind = 0; kind < 4; kind++)
        {
            var shapes = ShapeFactory.Create(gesture, kind);
            Require(shapes.Count == (kind == 3 ? 2 : 1) && shapes.All(s => s.DrawingAttributes.IgnorePressure && !s.DrawingAttributes.FitToCurve),
                "Shape construction did not produce geometric strokes.");
        }
        Directory.CreateDirectory(root);
        var page = Guid.NewGuid(); var book = Guid.NewGuid(); var original = SampleData();
        var legacy = original with { Height = null, IsHighlighter = false };
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(root, "library.db")};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE notebooks(id TEXT PRIMARY KEY,name TEXT NOT NULL);
                CREATE TABLE pages(id TEXT PRIMARY KEY,notebook_id TEXT NOT NULL REFERENCES notebooks(id),name TEXT NOT NULL,position INTEGER NOT NULL,view_json TEXT);
                CREATE TABLE strokes(page_id TEXT NOT NULL REFERENCES pages(id),id TEXT NOT NULL,ordinal INTEGER NOT NULL,data_json TEXT NOT NULL,PRIMARY KEY(page_id,id));
                CREATE TABLE app_state(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                INSERT INTO notebooks VALUES($book,'Legacy notebook');
                INSERT INTO pages VALUES($page,$book,'Legacy lecture',0,$view);
                INSERT INTO strokes VALUES($page,$stroke,0,$data);
                INSERT INTO app_state VALUES('last_page',$page);
                INSERT INTO app_state VALUES('clean_shutdown','false');
                PRAGMA user_version=1;
                """;
            command.Parameters.AddWithValue("$book", book.ToString()); command.Parameters.AddWithValue("$page", page.ToString());
            command.Parameters.AddWithValue("$stroke", legacy.Id.ToString()); command.Parameters.AddWithValue("$data", JsonSerializer.Serialize(legacy));
            command.Parameters.AddWithValue("$view", JsonSerializer.Serialize(new PageView(1.8, -120, -300))); command.ExecuteNonQuery();
        }
        using (var db = new NotebookDatabase(root))
        {
            Require(db.Snapshot.RecoveredSession && db.Snapshot.LastPageId == page && db.LoadPage(page).Strokes[0].Points.SequenceEqual(original.Points),
                "Migration lost selection, recovery state, coordinates, or pressure.");
            Require(File.Exists(Path.Combine(root, "library-v02-backup.db")), "Migration did not create a v0.2 backup.");
            db.SaveNotebookPaper(book, a4);
            db.SaveTexts(page, [new PageText(Guid.NewGuid(), 45, 60, 350, 20, "#FF24324A", "Conservation of energy")]);
            db.SavePreferences(new WorkspacePreferences(true, false, true, [page], [page]));
            db.MarkClean();
        }
        using (var db = new NotebookDatabase(root))
        {
            Require(db.LoadPage(page).Paper == a4 && db.LoadPage(page).Texts?.Single().Text == "Conservation of energy", "Paper or text was lost after restart.");
            Require(db.Snapshot.Preferences?.Dark == true && db.Snapshot.Preferences.ToolbarBottom && db.Snapshot.Preferences.Favorites!.Contains(page),
                "Workspace preferences did not survive restart.");
            Require(db.Snapshot.Pages[0].View == new PageView(1.8, -120, -300), "Migration changed the saved viewport.");
            db.MarkClean();
        }
        using var backup = new SqliteConnection($"Data Source={Path.Combine(root, "library-v02-backup.db")};Mode=ReadOnly;Pooling=False");
        backup.Open();
        using var check = backup.CreateCommand(); check.CommandText = "PRAGMA user_version;";
        Require(Convert.ToInt32(check.ExecuteScalar(), CultureInfo.InvariantCulture) == 1, "The migration backup was overwritten.");
    }
    private static void CheckNewWorkspace(MainWindow window, PenCanvas ink, PageViewport viewport, string root)
    {
        var before = InkMapper.Capture(ink.Strokes).Strokes[0];
        var a4 = PaperSettings.Default with { Format = PaperFormat.A4, Landscape = true, Pattern = PaperPattern.Engineering, Spacing = 18 };
        window.ApplyPaper(a4, entireNotebook: true);
        Require(viewport.PaperWidth == a4.Width && viewport.PaperHeight == a4.Height &&
            InkMapper.Capture(ink.Strokes).Strokes[0].Points.SequenceEqual(before.Points), "Paper settings changed existing ink.");
        foreach (var name in new[] { "PenButton", "HighlighterButton", "EraserButton", "LassoButton", "ShapeButton", "TextButton", "CalculateButton", "HandButton" })
        {
            ((RadioButton)window.FindName(name)).IsChecked = true;
            window.CloseToolInspector();
            var count = ink.Strokes.Count; var objects = ink.Children.Count;
            var finger = new Finger(41) { Position = new Point(100, 150) };
            finger.Send(ink, UIElement.PreviewTouchDownEvent); finger.Position = new Point(140, 190);
            finger.Send(ink, UIElement.PreviewTouchMoveEvent); finger.Send(ink, UIElement.PreviewTouchUpEvent);
            Require(ink.Strokes.Count == count && ink.Children.Count == objects, $"Touch created content using {name}.");
        }
        ((RadioButton)window.FindName("HighlighterButton")).IsChecked = true;
        Require(ink.DefaultDrawingAttributes.IsHighlighter && ink.DefaultDrawingAttributes.IgnorePressure, "Highlighter mode did not use translucent ink.");
        Require(((TextBlock)window.FindName("ThicknessValue")).Text == "18", "Tool thickness label did not match its slider.");
        ((RadioButton)window.FindName("EraserButton")).IsChecked = true;
        ((ComboBox)window.FindName("EraserModePicker")).SelectedIndex = 1;
        Require(ink.EditingMode == InkCanvasEditingMode.EraseByPoint, "Precision eraser did not activate.");
        var original = ink.Strokes[0]; var cloneA = original.Clone(); var cloneB = original.Clone();
        ink.Strokes.Replace(original, new StrokeCollection { cloneA, cloneB });
        Require(InkMapper.Identity(cloneA) != InkMapper.Identity(cloneB) && InkMapper.Identity(cloneA) != before.Id, "Eraser fragments reused a stroke ID.");
        ((Button)window.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Strokes.Count == 1 && InkMapper.Identity(ink.Strokes[0]) == before.Id, "Precision eraser could not undo.");
        ((RadioButton)window.FindName("TextButton")).IsChecked = true;
        window.CloseToolInspector();
        ink.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.PreviewMouseDownEvent, Source = ink });
        Require(ink.Children.Count == 1 && ink.Children[0] is TextBox, "Text tool did not create an editable object.");
        ((TextBox)ink.Children[0]).Text = "Lecture notes";
        ((ComboBox)window.FindName("TextSizePicker")).SelectedIndex = 2;
        Require(((TextBox)ink.Children[0]).FontSize == 28, "Text size did not update the selected text object.");
        ((RadioButton)window.FindName("PenButton")).IsChecked = true;
        ((Button)window.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Children.Count == 0, "Text creation was not undoable.");
        ((Button)window.FindName("RedoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Children.OfType<TextBox>().Single().Text == "Lecture notes" && ink.Children.OfType<TextBox>().Single().FontSize == 28, "Text redo lost content or size.");
        Require(!ink.DefaultDrawingAttributes.IgnorePressure && ink.DefaultDrawingAttributes.FitToCurve, "Returning to pen changed the pressure path.");
        ((ComboBox)window.FindName("ColorPicker")).SelectedIndex = 0;
        var penSettings = ink.DefaultDrawingAttributes.Clone();
        window.SetDarkTheme(true);
        Require(((SolidColorBrush)Application.Current.Resources["Workspace"]).Color.R < 40 && ink.DefaultDrawingAttributes.Color == penSettings.Color,
            "Dark mode changed the paper's writing color.");
        window.SetDarkTheme(false);
        window.ToggleFocus();
        Require(((FrameworkElement)window.FindName("NavigationPanel")).Visibility == Visibility.Collapsed &&
            ((FrameworkElement)window.FindName("TabStrip")).Visibility == Visibility.Collapsed, "Focus mode did not reclaim writing space.");
        window.ToggleFocus();
        Await(window.FlushAsync());
        Directory.CreateDirectory(root);
        Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, "portrait.png"), 960, 1440));
        Require(((FrameworkElement)window.FindName("NavigationPanel")).Visibility == Visibility.Collapsed, "Portrait layout did not collapse the library.");
        var host = (FrameworkElement)window.FindName("PageHost");
        var background = (FrameworkElement)window.FindName("PaperBackground");
        Require(Math.Abs(host.ActualWidth / host.ActualHeight - a4.Width / a4.Height) < .0001 &&
            Math.Abs(background.ActualHeight - a4.Height) < .1, "Paper was clipped to the viewport before zooming.");
        var probe = host.TranslatePoint(new Point(12.7, a4.Height - 9.3), (UIElement)window.Content);
        var bitmap = new BitmapImage(new Uri(Path.GetFullPath(Path.Combine(root, "portrait.png"))));
        var pixel = new byte[4]; bitmap.CopyPixels(new Int32Rect((int)probe.X, (int)probe.Y, 1, 1), pixel, 4, 0);
        Require(pixel[0] > 240 && pixel[1] > 240 && pixel[2] > 240, "The bottom of the paper disappeared after fitting the page.");
        Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, "compact.png"), 720, 700));
        var toolbar = (FrameworkElement)window.FindName("ToolbarFrame");
        Require(toolbar.ActualWidth < 720 && toolbar.ActualHeight >= 44, "Compact toolbar overflowed.");
        // Keep the original regression fixture in its standard layout.
        var content = (FrameworkElement)window.Content; content.Measure(new Size(1280, 900)); content.Arrange(new Rect(0, 0, 1280, 900)); content.UpdateLayout();
    }
}
