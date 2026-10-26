using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckPageScrolling(string root)
    {
        using (var db = new NotebookDatabase(root))
        {
            var book = db.Snapshot.Notebooks[0].Id;
            for (var i = 1; i < 40; i++) db.CreatePage(new PageSummary(Guid.NewGuid(), book, $"Page {i + 1}", i, null));
            db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace();
        var mode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -30000; window.Top = -30000;
        window.Width = 960; window.Height = 700; window.Show();
        ((FrameworkElement)window.FindName("NavigationPanel")).Visibility = Visibility.Visible; ArrangeWorkspace(window);
        try
        {
            var list = (ListBox)window.FindName("PageList");
            var viewer = Descendants<ScrollViewer>(list).First();
            Require(VirtualizingPanel.GetScrollUnit(list) == ScrollUnit.Pixel && viewer.ScrollableHeight > 1000,
                "The page list did not use a scrollable pixel viewport.");
            viewer.ScrollToVerticalOffset(0); list.UpdateLayout();
            var wheel = new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -40) { RoutedEvent = Mouse.PreviewMouseWheelEvent };
            list.RaiseEvent(wheel); Await(Task.Delay(300)); list.UpdateLayout();
            Require(wheel.Handled && viewer.VerticalOffset > 0 && viewer.VerticalOffset < 100,
                $"A small wheel step snapped a page instead of moving by pixels: {viewer.VerticalOffset}.");
            var before = viewer.VerticalOffset;
            list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            Await(Task.Delay(300)); list.UpdateLayout();
            Require(viewer.VerticalOffset > before, "Consecutive wheel input did not advance the page list.");
            list.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            list.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = Mouse.PreviewMouseDownEvent });
            viewer.ScrollToVerticalOffset(275); list.UpdateLayout(); Await(Task.Delay(250)); list.UpdateLayout();
            Require(Math.Abs(viewer.VerticalOffset - 275) < 1, "Wheel easing fought a subsequent direct scroll.");
        }
        finally { Await(window.ShutdownLibraryAsync()); window.Close(); Application.Current.ShutdownMode = mode; }
    }
    private static void CheckLibraryActions(string root)
    {
        Guid book, first, second;
        var stroke = SampleData();
        var text = new PageText(Guid.NewGuid(), 80, 90, 300, 24, "#FF24324A", "Copy me independently");
        var paper = PaperSettings.Default with { Pattern = PaperPattern.Engineering };
        using (var db = new NotebookDatabase(root))
        {
            book = db.Snapshot.Notebooks[0].Id; first = db.Snapshot.LastPageId; second = Guid.NewGuid();
            db.ApplyDelta(first, [new StoredStroke(stroke, 0)], []); db.SaveTexts(first, [text]); db.SavePaper(first, paper);
            db.SaveView(first, new PageView(1.4, -50, -80));
            db.CreatePage(new PageSummary(second, book, "Second page", 1, null, paper)); db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync());
        try
        {
            var folder = Await(window.CreateFolderAsync("Copies"));
            Await(window.CopyNotebookAsync(book));
            var copy = Await(window.PasteNotebookAsync(folder));
            var copy2 = Await(window.PasteNotebookAsync(folder));
            Require(copy != copy2 && window.CanPasteNotebook, "Repeated notebook paste reused the notebook identity.");
            Await(window.ActivateNotebookAsync(copy)); window.CloseAllNotebookTabs();
            Require(window.IsHomeVisible && window.CurrentPageId is null && ((ListBox)window.FindName("DocumentTabs")).Items.Count == 0,
                "Closing all tabs did not leave a document-free library.");
            Require(Await(window.ShutdownLibraryAsync()), "Library actions did not save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        try
        {
            Require(reopened.IsHomeVisible && reopened.CurrentPageId is null && ((ListBox)reopened.FindName("DocumentTabs")).Items.Count == 0,
                "Restart reopened a deliberately closed tab.");
            Await(reopened.ActivateNotebookAsync(book));
            Await(reopened.DeletePageAsync(first));
            Require(reopened.CurrentPageId == second, "Deleting the active page did not select its neighbor.");
            Await(reopened.DeletePageAsync(second));
            Require(reopened.IsHomeVisible && reopened.CurrentPageId is null, "Deleting the last page left a stale editor.");
            Await(reopened.ActivateNotebookAsync(book));
            Require(reopened.CurrentPageId is Guid replacement && replacement != first && replacement != second,
                "Opening an empty notebook did not create a fresh blank page.");
            Await(reopened.CopyNotebookAsync(book));
            Await(reopened.DeleteNotebookAsync(book));
            Require(reopened.IsHomeVisible, "Deleting the active notebook did not return to the library.");
            var restoredCopy = Await(reopened.PasteNotebookAsync());
            Require(restoredCopy != book, "A copied notebook could not be pasted after deleting its source.");
            Require(Await(reopened.ShutdownLibraryAsync()), "Deletion changes did not save.");
        }
        finally { Await(reopened.ShutdownLibraryAsync()); }
        List<Guid> books;
        using (var db = new NotebookDatabase(root))
        {
            Require(db.Snapshot.Notebooks.All(n => n.Id != book) && db.Snapshot.Pages.All(p => p.Id != first && p.Id != second), "Deleted notes returned after restart.");
            var copies = db.Snapshot.Notebooks.Where(n => n.FolderId is not null).ToList();
            Require(copies.Count == 2 && copies.Select(n => n.Name).Distinct().Count() == 2, "Pasting did not preserve destination or unique copy names.");
            foreach (var copy in copies)
            {
                var pages = db.Snapshot.Pages.Where(p => p.NotebookId == copy.Id).OrderBy(p => p.Position).ToList();
                var doc = db.LoadPage(pages[0].Id);
                Require(pages.Count == 2 && pages[1].Name == "Second page" && pages[0].View == new PageView(1.4, -50, -80) && doc.Paper == paper,
                    "Notebook copy lost page order, paper, or view.");
                Require(doc.Strokes.Single().Id != stroke.Id && doc.Strokes.Single().Points.SequenceEqual(stroke.Points) &&
                    doc.Texts!.Single().Id != text.Id && doc.Texts!.Single().Text == text.Text, "Notebook copy lost content or reused object IDs.");
            }
            var firstCopyPage = db.Snapshot.Pages.First(p => p.NotebookId == copies[0].Id).Id;
            var secondCopyPage = db.Snapshot.Pages.First(p => p.NotebookId == copies[1].Id).Id;
            db.ReplacePage(firstCopyPage, new PageDocument(PageDocument.CurrentVersion, paper.Width, paper.Height, [], paper));
            Require(db.LoadPage(secondCopyPage).Strokes.Count == 1, "Editing a notebook copy changed another copy.");
            books = db.Snapshot.Notebooks.Select(n => n.Id).ToList(); db.MarkClean();
        }
        var empty = new MainWindow(root); Await(empty.InitializeLibraryAsync());
        try { foreach (var id in books) Await(empty.DeleteNotebookAsync(id)); Require(Await(empty.ShutdownLibraryAsync()), "Empty library did not save."); }
        finally { Await(empty.ShutdownLibraryAsync()); }
        using (var db = new NotebookDatabase(root))
        {
            Require(db.Snapshot.Notebooks.Count == 0 && db.Snapshot.Pages.Count == 0 && db.Snapshot.LastPageId == Guid.Empty,
                "An empty library recreated a deleted notebook."); db.MarkClean();
        }
        var final = new MainWindow(root); Await(final.InitializeLibraryAsync());
        Require(final.IsHomeVisible && final.CurrentPageId is null, "An empty library failed to open.");
        Await(final.CreateNotebookAsync("A fresh start"));
        Require(final.CurrentPageId is not null && !final.IsHomeVisible, "Creating a notebook in an empty library failed.");
        Require(Await(final.ShutdownLibraryAsync()), "Fresh notebook did not save.");
    }
}
