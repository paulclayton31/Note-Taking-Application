using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

internal static partial class Program
{
    private static void ArrangeWorkspace(MainWindow window, double width = 1280, double height = 900)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        root.UpdateLayout();
    }
    private static async Task WaitForPageAsync(MainWindow window, Guid page)
    {
        var until = DateTime.UtcNow.AddSeconds(5);
        while (window.CurrentPageId != page || !((ListBox)window.FindName("DocumentTabs")).IsEnabled)
        {
            if (DateTime.UtcNow >= until) throw new InvalidOperationException("Notebook navigation did not finish.");
            await Task.Delay(10);
        }
    }
    private static Rect ToolbarBounds(MainWindow window)
    {
        var frame = (FrameworkElement)window.FindName("ToolbarFrame");
        return frame.TransformToAncestor((FrameworkElement)window.FindName("MainStage")).TransformBounds(new Rect(frame.RenderSize));
    }
    private static void RequireToolbarReachable(MainWindow window)
    {
        var bounds = ToolbarBounds(window); var stage = (FrameworkElement)window.FindName("MainStage");
        Require(bounds.Left >= 11.9 && bounds.Top >= 11.9 && bounds.Right <= stage.ActualWidth - 11.9 && bounds.Bottom <= stage.ActualHeight - 11.9,
            $"The toolbar left the workspace: {bounds}; stage {stage.RenderSize}.");
    }
    private static ToolbarPosition ReadToolbarPosition(MainWindow window)
    {
        var bounds = ToolbarBounds(window); var stage = (FrameworkElement)window.FindName("MainStage");
        return new ToolbarPosition((bounds.Left - 12) / Math.Max(1, stage.ActualWidth - bounds.Width - 24),
            (bounds.Top - 12) / Math.Max(1, stage.ActualHeight - bounds.Height - 24));
    }
    private static void CheckNotebookTabsAndToolbar(string root)
    {
        Guid bookA, bookB, first, second, third, other;
        using (var db = new NotebookDatabase(root))
        {
            bookA = db.Snapshot.Notebooks[0].Id; first = db.Snapshot.LastPageId;
            second = Guid.NewGuid(); third = Guid.NewGuid(); bookB = Guid.NewGuid(); other = Guid.NewGuid();
            db.RenameNotebook(bookA, "Physics");
            db.CreatePage(new PageSummary(second, bookA, "Lecture 2", 1, null));
            db.CreatePage(new PageSummary(third, bookA, "Lecture 3", 2, null));
            db.CreateNotebook(new NotebookSummary(bookB, "Calculus"), other);
            // Preferences written by preview.1 contain one tab for every visited page.
            db.SavePreferences(new WorkspacePreferences(ToolbarBottom: true, OpenPages: [first, second, third, other, first]));
            db.SelectPage(second); db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); ArrangeWorkspace(window);
        var tabs = (ListBox)window.FindName("DocumentTabs");
        Require(tabs.Items.Count == 2 && tabs.Items.OfType<NotebookSummary>().Select(n => n.Id).Distinct().Count() == 2,
            "Old page tabs were not collapsed into one tab per notebook.");
        Require(((NotebookSummary)tabs.SelectedItem).Id == bookA && window.CurrentPageId == second, "Migration lost the active notebook or page.");
        Require(tabs.Items.OfType<NotebookSummary>().Any(n => n.Name == "Physics"), "Tabs show page titles instead of notebook names.");
        foreach (var page in new[] { first, third, second })
        {
            var list = (ListBox)window.FindName("PageList");
            list.SelectedItem = list.Items.OfType<NavigationPage>().Single(p => p.Id == page);
            Await(WaitForPageAsync(window, page));
            Require(tabs.Items.Count == 2 && ((NotebookSummary)tabs.SelectedItem).Id == bookA, "Selecting a page opened a new tab.");
        }
        var lastA = Await(window.CreatePageAsync("Study notes"));
        Require(tabs.Items.Count == 2, "Creating a page opened a new tab.");
        var search = (TextBox)window.FindName("SearchBox"); search.Text = "Study";
        tabs.SelectedItem = tabs.Items.OfType<NotebookSummary>().Single(n => n.Id == bookB);
        Await(WaitForPageAsync(window, other));
        Require(search.Text == "" && ((ListBox)window.FindName("PageList")).Items.Count == 1, "A previous notebook's search hid the new notebook's pages.");
        tabs.SelectedItem = tabs.Items.OfType<NotebookSummary>().Single(n => n.Id == bookA);
        Await(WaitForPageAsync(window, lastA));
        for (var i = 0; i < 6; i++) lastA = Await(window.CreatePageAsync());
        Await(window.ActivateNotebookAsync(bookB)); Await(window.ActivateNotebookAsync(bookA));
        Require(window.CurrentPageId == lastA && tabs.Items.Count == 2, "Notebook tabs forgot the last page after session-cache eviction.");
        Await(window.CloseNotebookTabAsync(bookB));
        Require(tabs.Items.Count == 1 && window.CurrentPageId == lastA, "Closing an inactive notebook changed the current page.");
        var picker = (ComboBox)window.FindName("NotebookPicker");
        picker.SelectedItem = picker.Items.OfType<NotebookSummary>().Single(n => n.Id == bookB);
        Await(WaitForPageAsync(window, other));
        Require(tabs.Items.Count == 2, "Reopening a notebook did not restore its tab.");
        Await(window.CloseNotebookTabAsync(bookB));
        Require(window.CurrentPageId == lastA && tabs.Items.Count == 1, "Closing the active tab did not restore the neighboring notebook's last page.");
        Await(window.CloseNotebookTabAsync(bookA));
        Require(tabs.Items.Count == 1, "Closing the only tab left the workspace without a document.");

        ArrangeWorkspace(window);
        // Attach the real window to an offscreen HWND for input hit testing.
        // Rendering-only tests have IsVisible=false and cannot exercise this path.
        var shutdownMode = Application.Current.ShutdownMode;
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Left = -30000; window.Top = -30000; window.Width = 1280; window.Height = 900;
        window.Show(); ArrangeWorkspace(window);
        var grip = (Thumb)window.FindName("ToolbarGrip");
        var stage = (FrameworkElement)window.FindName("MainStage");
        var rootElement = (FrameworkElement)window.Content;
        var point = grip.TranslatePoint(new Point(grip.ActualWidth / 2, grip.ActualHeight / 2), rootElement);
        var hit = rootElement.InputHitTest(point) as DependencyObject;
        Require(grip.ActualWidth >= 44 && grip.ActualHeight >= 44 && hit is not null && (hit == grip || grip.IsAncestorOf(hit)),
            "The toolbar grip has no usable hit-test surface.");
        window.Hide(); // Later maximize/focus checks must stay invisible.
        Require(Math.Abs(ToolbarBounds(window).Bottom - (stage.ActualHeight - 12)) < .1, "Legacy bottom docking was not restored.");
        var before = ToolbarBounds(window);
        grip.RaiseEvent(new DragDeltaEventArgs(-90, -120) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(-90, -120, false) { RoutedEvent = Thumb.DragCompletedEvent });
        var moved = ToolbarBounds(window);
        Require(Math.Abs(moved.Left - before.Left + 90) < .1 && Math.Abs(moved.Top - before.Top + 120) < .1, "Mouse toolbar dragging did not move the palette.");
        var viewport = (PageViewport)window.FindName("Viewport"); var viewBefore = viewport.View;
        var ink = (PenCanvas)window.FindName("InkSurface"); var inkCount = ink.Strokes.Count;
        var finger = new Finger(81) { Position = new Point(250, 250) };
        finger.Send(grip, UIElement.PreviewTouchDownEvent);
        finger.Position = new Point(300, 210); finger.Send(grip, UIElement.PreviewTouchMoveEvent);
        finger.Send(grip, UIElement.PreviewTouchUpEvent);
        var touched = ToolbarBounds(window);
        Require(Math.Abs(touched.Left - moved.Left - 50) < .1 && Math.Abs(touched.Top - moved.Top + 40) < .1,
            "Touch did not drag the toolbar.");
        Require(viewport.View == viewBefore && ink.Strokes.Count == inkCount, "Moving the toolbar panned the paper or created ink.");
        grip.RaiseEvent(new DragDeltaEventArgs(100000, 100000) { RoutedEvent = Thumb.DragDeltaEvent });
        RequireToolbarReachable(window);
        grip.RaiseEvent(new DragDeltaEventArgs(-100000, -100000) { RoutedEvent = Thumb.DragDeltaEvent });
        RequireToolbarReachable(window);
        ((Button)window.FindName("DockToolbarBottomButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Math.Abs(ToolbarBounds(window).Bottom - stage.ActualHeight + 12) < .1, "Bottom dock action failed.");
        ((Button)window.FindName("DockToolbarTopButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(Math.Abs(ToolbarBounds(window).Top - 12) < .1, "Top dock action failed.");
        grip.RaiseEvent(new DragDeltaEventArgs(-60, 130) { RoutedEvent = Thumb.DragDeltaEvent });
        grip.RaiseEvent(new DragCompletedEventArgs(-60, 130, false) { RoutedEvent = Thumb.DragCompletedEvent });
        var position = ReadToolbarPosition(window);
        Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, "toolbar-portrait.png"), 960, 1440));
        RequireToolbarReachable(window);
        var portraitPosition = ReadToolbarPosition(window);
        Require(Math.Abs(portraitPosition.Horizontal - position.Horizontal) < .001 && Math.Abs(portraitPosition.Vertical - position.Vertical) < .001,
            "Resizing reset the toolbar placement.");
        window.ToggleFocus(); ArrangeWorkspace(window, 960, 1440); RequireToolbarReachable(window);
        window.ToggleFocus(); ArrangeWorkspace(window, 960, 1440);
        var focusPosition = ReadToolbarPosition(window);
        Require(Math.Abs(focusPosition.Horizontal - position.Horizontal) < .001 && Math.Abs(focusPosition.Vertical - position.Vertical) < .001,
            "Focus mode reset the toolbar placement.");
        ((Button)window.FindName("PaperButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); ArrangeWorkspace(window, 960, 1440);
        RequireToolbarReachable(window);
        Await(PreviewCapture.CaptureAsync(window, Path.Combine(root, "toolbar-compact.png"), 640, 700));
        RequireToolbarReachable(window);
        Require(Await(window.ShutdownLibraryAsync()), "The modified workspace could not be saved.");
        window.Close();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        Application.Current.ShutdownMode = shutdownMode;

        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync()); ArrangeWorkspace(reopened);
        Require(reopened.CurrentPageId == lastA && ((ListBox)reopened.FindName("DocumentTabs")).Items.Count == 1,
            "Restart did not preserve the current notebook and closed-tab state.");
        var restoredPosition = ReadToolbarPosition(reopened);
        Require(Math.Abs(restoredPosition.Horizontal - position.Horizontal) < .001 && Math.Abs(restoredPosition.Vertical - position.Vertical) < .001,
            "Restart did not preserve the toolbar's saved placement.");
        Await(reopened.ActivateNotebookAsync(bookB));
        Require(reopened.CurrentPageId == other && ((ListBox)reopened.FindName("DocumentTabs")).Items.Count == 2, "A closed notebook forgot its last page after restart.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Reopened workspace did not close cleanly.");
        using var stored = new NotebookDatabase(root);
        Require(stored.Snapshot.Preferences?.OpenPages is null && stored.Snapshot.Preferences?.OpenNotebooks?.Count == 2,
            "Notebook tabs were saved using the old page-tab preference.");
        stored.MarkClean();
    }
}
