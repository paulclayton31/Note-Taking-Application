using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

internal static partial class Program
{
    private static void CheckLibraryStorage(string root)
    {
        Guid first, second, third, copy, book, folder, child;
        var ink = SampleData();
        var paper = PaperSettings.Default with { Format = PaperFormat.Legal, Landscape = true, Pattern = PaperPattern.Engineering, Background = "#FFF6F0DE" };
        var text = new PageText(Guid.NewGuid(), 70, 90, 340, 28, "#FF24324A", "Energy balance");
        using (var db = new NotebookDatabase(root))
        {
            first = db.Snapshot.LastPageId; book = db.Snapshot.Notebooks[0].Id;
            second = Guid.NewGuid(); third = Guid.NewGuid(); copy = Guid.NewGuid();
            db.CreatePage(new PageSummary(second, book, "Second", 1, null)); db.CreatePage(new PageSummary(third, book, "Third", 2, null));
            db.SavePaper(first, paper); db.SaveTexts(first, [text]); db.ApplyDelta(first, [new StoredStroke(ink, 0)], []);
            db.SaveView(first, new PageView(1.7, -65, -100));
            var duplicated = db.DuplicatePage(first, copy, "Lecture copy");
            Require(duplicated.Page.Position == 1 && duplicated.Document.Paper == paper &&
                duplicated.Document.Width == paper.Width && duplicated.Document.Height == paper.Height, "Duplication lost paper or insertion order.");
            Require(duplicated.Document.Strokes.Single().Id != ink.Id && duplicated.Document.Strokes.Single().Points.SequenceEqual(ink.Points) &&
                duplicated.Document.Texts!.Single().Id != text.Id && duplicated.Document.Texts!.Single().Text == text.Text, "Duplication lost content or reused object identities.");
            Require(duplicated.Page.View == new PageView(1.7, -65, -100), "Duplication lost page position.");
            db.ReorderPages(book, [third, copy, first, second]);
            try { db.ReorderPages(book, [first, second]); throw new Exception("Partial page order accepted."); } catch (InvalidOperationException) { }
            folder = Guid.NewGuid(); child = Guid.NewGuid();
            db.CreateFolder(new FolderSummary(folder, "Fall semester")); db.CreateFolder(new FolderSummary(child, "Physics", folder));
            db.MoveNotebook(book, child); db.FavoriteNotebook(book, true); db.SelectPage(copy, 1792800000000);
            try { db.MoveFolder(folder, child); throw new Exception("Folder cycle accepted."); } catch (InvalidOperationException) { }
            db.MarkClean();
        }
        using (var db = new NotebookDatabase(root))
        {
            Require(db.Snapshot.Pages.Where(p => p.NotebookId == book).OrderBy(p => p.Position).Select(p => p.Id).SequenceEqual(new[] { third, copy, first, second }),
                "Page order did not survive reopening.");
            var notebook = db.Snapshot.Notebooks.Single(n => n.Id == book);
            Require(notebook.FolderId == child && notebook.Favorite && notebook.LastOpened == 1792800000000 && db.Snapshot.Folders?.Count == 2,
                "Folders, stars, or recents did not survive reopening.");
            db.ApplyDelta(copy, [], [db.LoadPage(copy).Strokes.Single().Id]);
            Require(db.LoadPage(first).Strokes.Single().Id == ink.Id, "Editing a duplicate changed the source page.");
            db.MarkClean();
        }
        var legacyRoot = root + "-v2"; Directory.CreateDirectory(legacyRoot);
        var legacyBook = Guid.NewGuid(); var legacyPage = Guid.NewGuid();
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(legacyRoot, "library.db")};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE notebooks(id TEXT PRIMARY KEY,name TEXT NOT NULL,paper_json TEXT);
                CREATE TABLE pages(id TEXT PRIMARY KEY,notebook_id TEXT NOT NULL,name TEXT NOT NULL,position INTEGER NOT NULL,view_json TEXT,paper_json TEXT,objects_json TEXT);
                CREATE TABLE strokes(page_id TEXT NOT NULL,id TEXT NOT NULL,ordinal INTEGER NOT NULL,data_json TEXT NOT NULL,PRIMARY KEY(page_id,id));
                CREATE TABLE app_state(key TEXT PRIMARY KEY,value TEXT NOT NULL);
                INSERT INTO notebooks VALUES($book,'Existing physics',$paper);
                INSERT INTO pages VALUES($page,$book,'Existing lecture',0,NULL,$paper,NULL);
                INSERT INTO strokes VALUES($page,$stroke,0,$ink);
                INSERT INTO app_state VALUES('last_page',$page);
                PRAGMA user_version=2;
                """;
            command.Parameters.AddWithValue("$book", legacyBook.ToString()); command.Parameters.AddWithValue("$page", legacyPage.ToString());
            command.Parameters.AddWithValue("$paper", JsonSerializer.Serialize(paper)); command.Parameters.AddWithValue("$stroke", ink.Id.ToString());
            command.Parameters.AddWithValue("$ink", JsonSerializer.Serialize(ink)); command.ExecuteNonQuery();
        }
        using (var db = new NotebookDatabase(legacyRoot))
        {
            Require(db.LoadPage(legacyPage).Strokes.Single().Points.SequenceEqual(ink.Points) && db.LoadPage(legacyPage).Paper == paper,
                "Schema 2 migration changed ink or paper.");
            Require(File.Exists(Path.Combine(legacyRoot, "library-preview2-backup.db")), "Schema 2 migration did not create a backup.");
            db.MarkClean();
        }
        using var backup = new SqliteConnection($"Data Source={Path.Combine(legacyRoot, "library-preview2-backup.db")};Mode=ReadOnly;Pooling=False");
        backup.Open(); using var version = backup.CreateCommand(); version.CommandText = "PRAGMA user_version;";
        Require(Convert.ToInt32(version.ExecuteScalar()) == 2, "The preview.2 backup was not preserved.");
        using var writer = new AutosaveWriter(root + "-mutation-errors"); writer.Initialized.GetAwaiter().GetResult();
        try
        {
            writer.MutateAsync<bool>(_ => throw new InvalidOperationException("Invalid folder move")).WaitAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
            throw new Exception("An invalid library operation was accepted.");
        }
        catch (InvalidOperationException ex) when (ex.Message == "Invalid folder move") { }
        Require(writer.FlushAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult() && writer.Pending == 0, "A rejected library operation blocked subsequent saves.");
        writer.StopAsync().GetAwaiter().GetResult();
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T value) yield return value;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static void ClickHomeCard(ItemsControl control, Guid id)
    {
        var button = Descendants<Button>(control).First(b => b.Tag is Guid value && value == id && b.ContextMenu is null);
        Click(button);
    }
    private static void CheckHomePensZoomAndPages(string root)
    {
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); ArrangeWorkspace(window, 1440, 1000);
        Require(window.IsHomeVisible, "The new library did not open at Home.");
        var home = (LibraryHomeView)window.FindName("HomeView");
        var header = (FrameworkElement)window.FindName("UnifiedHeader"); var tabs = (FrameworkElement)window.FindName("DocumentTabs");
        Require(header.ActualHeight <= 52 && tabs.TranslatePoint(new Point(), (UIElement)window.Content).Y < 52, "Tabs still occupy a separate header row.");
        var parent = Await(window.CreateFolderAsync("Semester"));
        var child = Await(window.CreateFolderAsync("Physics", parent));
        var firstPage = Await(window.CreateNotebookAsync("Mechanics", child));
        var notebook = ((ComboBox)window.FindName("NotebookPicker")).Items.OfType<NotebookSummary>().Single(n => n.Name == "Mechanics");
        window.SetNotebookFavorite(notebook.Id, true);
        var ink = (PenCanvas)window.FindName("InkSurface"); ink.Strokes.Add(SampleStroke());
        var original = InkMapper.Capture(ink.Strokes);
        var viewport = (PageViewport)window.FindName("Viewport");
        window.ApplyPaper(PaperSettings.Default with { Format = PaperFormat.A4, Pattern = PaperPattern.Dots }, false);
        viewport.SetView(new PageView(1.4, -50, -80)); var view = viewport.View;
        window.ShowHome(); ArrangeWorkspace(window, 1440, 1000);
        ClickHomeCard((ItemsControl)home.FindName("FolderCards"), parent); ArrangeWorkspace(window, 1440, 1000);
        ClickHomeCard((ItemsControl)home.FindName("FolderCards"), child); ArrangeWorkspace(window, 1440, 1000);
        Require(((ItemsControl)home.FindName("NotebookCards")).Items.OfType<HomeNotebookItem>().Single().Id == notebook.Id, "Nested folder navigation showed the wrong notebooks.");
        ClickHomeCard((ItemsControl)home.FindName("NotebookCards"), notebook.Id); Await(WaitForPageAsync(window, firstPage));
        Require(!window.IsHomeVisible && viewport.View == view && ink.Strokes.Count == 1, "Opening from Home lost page content or position.");
        window.ShowHome(); ArrangeWorkspace(window, 1440, 1000);
        Click((Button)home.FindName("StarredSection")); ArrangeWorkspace(window, 1440, 1000);
        Require(((ItemsControl)home.FindName("NotebookCards")).Items.OfType<HomeNotebookItem>().Single().Id == notebook.Id, "Starred notebooks filter failed.");
        Click((Button)home.FindName("LibrarySection"));
        ((TextBox)home.FindName("LibrarySearch")).Text = "Physics"; ArrangeWorkspace(window, 1440, 1000);
        Require(((ItemsControl)home.FindName("NotebookCards")).Items.OfType<HomeNotebookItem>().Any(n => n.Id == notebook.Id), "Home search did not find notebooks by folder path.");
        ((TextBox)home.FindName("LibrarySearch")).Clear();
        Await(window.MoveNotebookAsync(notebook.Id, null)); Await(window.MoveFolderAsync(child, null));
        try { Await(window.MoveFolderAsync(child, child)); throw new Exception("A cyclic folder move was allowed."); } catch (InvalidOperationException) { }
        window.RenameNotebook(notebook.Id, "Classical mechanics");
        window.ShowWorkspace(); ArrangeWorkspace(window, 1440, 1000);
        Require(((NotebookSummary)((ListBox)window.FindName("DocumentTabs")).SelectedItem).Name == "Classical mechanics", "Notebook rename did not update its tab.");
        CheckTabCloseLayout(window, notebook.Id, root);
        ((ComboBox)window.FindName("ColorPicker")).SelectedIndex = 3;
        ((Slider)window.FindName("ThicknessSlider")).Value = 4.5;
        ((RadioButton)window.FindName("BallStyleButton")).IsChecked = true;
        var preset = window.AddPen("Lab outline");
        ((Slider)window.FindName("ThicknessSlider")).Value = 2;
        ((RadioButton)window.FindName("FountainStyleButton")).IsChecked = true;
        window.ApplyPenPreset(preset.Id);
        Require(ink.DefaultDrawingAttributes.Width == 2 && !ink.DefaultDrawingAttributes.IgnorePressure && ink.DefaultDrawingAttributes.Color.G == 135,
            "Edits to a pen did not save automatically.");
        ((Slider)window.FindName("ThicknessSlider")).Value = 4.5;
        ((RadioButton)window.FindName("BallStyleButton")).IsChecked = true;
        Require(InkMapper.Capture(ink.Strokes).Strokes.Single().Points.SequenceEqual(original.Strokes.Single().Points) && ink.Strokes[0].DrawingAttributes.Color == Colors.Blue,
            "Changing presets altered existing handwriting.");
        // A dismissible popup needs a visible owner HWND for mouse capture.
        // Keep both the owner and popup invisible to the person using the desktop.
        var shutdownMode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -30000; window.Top = -30000;
        window.Width = 1440; window.Height = 1000; window.Show(); ArrangeWorkspace(window, 1440, 1000);
        var zoomPopup = (Popup)window.FindName("ZoomPopup"); ((UIElement)zoomPopup.Child).Opacity = 0;
        var beforeZoom = viewport.View; var center = new Point(viewport.ActualWidth / 2, viewport.ActualHeight / 2);
        var pageCenter = new Point((center.X - beforeZoom.X) / beforeZoom.Zoom, (center.Y - beforeZoom.Y) / beforeZoom.Zoom);
        Click((Button)window.FindName("ZoomButton")); Require(((Popup)window.FindName("ZoomPopup")).IsOpen, "Tapping zoom did not open touch controls.");
        ((Slider)window.FindName("ZoomSlider")).Value = 175;
        Require(Math.Abs(viewport.View.Zoom - 1.75) < .001 && Math.Abs((center.X - viewport.View.X) / viewport.View.Zoom - pageCenter.X) < .01,
            "Zoom slider did not preserve its center anchor.");
        Click((Button)window.FindName("FitWidthButton")); var widthZoom = viewport.View.Zoom;
        Click((Button)window.FindName("FitWholePageButton"));
        Require(viewport.View.Zoom <= widthZoom && !((Popup)window.FindName("ZoomPopup")).IsOpen, "Whole-page/width fit controls failed.");
        window.Hide();
        window.SetZoomPercent(1000); Require(Math.Abs(viewport.View.Zoom - PageView.MaxZoom) < .001, $"Zoom did not clamp to the supported range: {viewport.View.Zoom}.");
        window.SetZoomPercent(175);
        window.ToggleFocus(); ArrangeWorkspace(window, 1440, 1000);
        Require(Math.Abs(viewport.View.Zoom - 1.75) < .001, "Focus mode reset writing zoom.");
        window.ToggleFocus(); ArrangeWorkspace(window, 1440, 1000);
        var second = Await(window.CreatePageAsync("Problem set"));
        Await(window.ActivatePageAsync(firstPage));
        var copy = Await(window.DuplicatePageAsync(firstPage));
        Require(window.CurrentPageId == copy && ink.Strokes.Count == 1 && ((ListBox)window.FindName("DocumentTabs")).Items.Count == 2,
            "Duplication lost content or created a page tab.");
        var pageList = (ListBox)window.FindName("PageList");
        Require(pageList.Items.OfType<NavigationPage>().Select(p => p.Id).SequenceEqual(new[] { firstPage, copy, second }), "The duplicate was not inserted after its source.");
        ink.Strokes.Clear();
        Await(window.ActivatePageAsync(firstPage)); Require(ink.Strokes.Count == 1, "Editing the copy altered its source.");
        var currentView = viewport.View; var currentPage = window.CurrentPageId;
        Await(window.MovePageAsync(second, 0));
        Require(window.CurrentPageId == currentPage && viewport.View == currentView &&
            pageList.Items.OfType<NavigationPage>().Select(p => p.Id).SequenceEqual(new[] { second, firstPage, copy }), "Reordering changed the active page or its view.");
        window.ApplyPenPreset(preset.Id);
        window.ShowHome(); ArrangeWorkspace(window, 1440, 1000);
        window.SetDarkTheme(true); ArrangeWorkspace(window, 1440, 1000);
        Require(((SolidColorBrush)((Button)window.FindName("HomeButton")).Background).Color.R < 90, "The Home selection retained a light background in dark mode.");
        window.SetDarkTheme(false);
        Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "New library workflows did not save.");
        window.Close(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Application.Current.ShutdownMode = shutdownMode;
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync()); ArrangeWorkspace(reopened, 1440, 1000);
        Require(reopened.IsHomeVisible && reopened.Folders.Count == 2 && reopened.SavedPens.Any(p => p.Id == preset.Id), "Home, folders, or pen presets were lost after restart.");
        var reloadedInk = (PenCanvas)reopened.FindName("InkSurface");
        Require(reloadedInk.DefaultDrawingAttributes.Width == 4.5 && reloadedInk.DefaultDrawingAttributes.IgnorePressure, "Current pen settings were lost after restart.");
        Await(reopened.ActivateNotebookAsync(notebook.Id));
        var restored = ((ListBox)reopened.FindName("PageList")).Items.OfType<NavigationPage>().Select(p => p.Id);
        Require(restored.SequenceEqual(new[] { second, firstPage, copy }) && reloadedInk.Strokes.Count == 1, "Restart lost page order or the original handwriting.");
        Require(Await(reopened.ShutdownLibraryAsync()), "The reopened library did not finish saving.");
    }
    private static void CheckTabCloseLayout(MainWindow window, Guid notebook, string root)
    {
        var tabs = (ListBox)window.FindName("DocumentTabs");
        window.RenameNotebook(notebook, "Classical mechanics and engineering mathematics — lecture notebook");
        foreach (var width in new[] { 1440, 960, 640 })
        {
            Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, $"tab-layout-{width}.png"), width, 900));
            tabs.ScrollIntoView(tabs.SelectedItem); ArrangeWorkspace(window, width, 900);
            var close = Descendants<Button>(tabs).Single(b => b.Tag is Guid id && id == notebook);
            var glyph = Descendants<System.Windows.Shapes.Path>(close).Single();
            var bounds = glyph.TransformToAncestor(tabs).TransformBounds(new Rect(glyph.RenderSize));
            Require(close.ActualHeight == 40 && close.ActualWidth == 40 && glyph.ActualHeight == 12 && glyph.ActualWidth == 12 &&
                bounds.Top >= 0 && bounds.Bottom <= tabs.ActualHeight && bounds.Left >= 0 && bounds.Right <= tabs.ActualWidth + .1,
                $"The tab X is clipped at width {width}: {bounds}, tabs {tabs.RenderSize}.");
            var local = glyph.TransformToAncestor(close).TransformBounds(new Rect(glyph.RenderSize));
            Require(Math.Abs(local.Left + local.Width / 2 - close.ActualWidth / 2) < .1 &&
                Math.Abs(local.Top + local.Height / 2 - close.ActualHeight / 2) < .1, "The tab X is not centered in its hit target.");
            foreach (var scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var size = (int)Math.Ceiling(12 * scale);
                var bitmap = new RenderTargetBitmap(size, size, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                bitmap.Render(glyph); var pixels = new byte[size * size * 4]; bitmap.CopyPixels(pixels, size * 4, 0);
                var arms = new int[4];
                for (var y = 0; y < size; y++) for (var x = 0; x < size; x++)
                    if (pixels[(y * size + x) * 4 + 3] > 30) arms[(y < size / 2 ? 0 : 2) + (x < size / 2 ? 0 : 1)]++;
                Require(arms.All(count => count >= 4), $"A tab X arm is missing at {scale * 100}% display scale.");
            }
        }
        var previousPage = window.CurrentPageId;
        Click(Descendants<Button>(tabs).Single(b => b.Tag is Guid id && id == notebook));
        Await(WaitForPageAsync(window, window.CurrentPageId!.Value));
        Require(tabs.Items.Count == 1, "The close button did not close its notebook tab.");
        Await(window.ActivateNotebookAsync(notebook));
        Require(tabs.Items.Count == 2 && window.CurrentPageId == previousPage, "Closing a tab deleted its notebook or forgot its page.");
        window.RenameNotebook(notebook, "Classical mechanics");
        Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, "tab-layout-restored.png"), 1440, 1000));
    }
}
