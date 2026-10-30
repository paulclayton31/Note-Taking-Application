using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == "--crash-writer") return CrashWriter(args[1], args[2]);
        var root = Path.Combine(Environment.CurrentDirectory, "TestResults", "checks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var failures = new List<Exception>();
            void ExecuteCheck(string name, Action check)
            {
                try { check(); }
                catch (Exception ex) { Console.Error.WriteLine($"FAIL: {name}: {ex}"); failures.Add(ex); }
            }
            ExecuteCheck("CheckRoundTripAndValidation", () => CheckRoundTripAndValidation(root));
            ExecuteCheck("CheckHistory", () => CheckHistory());
            ExecuteCheck("CheckPaperAndMigration", () => CheckPaperAndMigration(Path.Combine(root, "migration")));
            ExecuteCheck("CheckLibraryStorage", () => CheckLibraryStorage(Path.Combine(root, "library-storage")));
            ExecuteCheck("CheckDatabase", () => CheckDatabase(Path.Combine(root, "database")));
            ExecuteCheck("CheckAutosave", () => CheckAutosave(Path.Combine(root, "writer")));
            ExecuteCheck("CheckCrashRecovery", () => CheckCrashRecovery(Path.Combine(root, "crash")));
            ExecuteCheck("CheckWindow", () => CheckWindow(Path.Combine(root, "ui"), args.FirstOrDefault()));
            ExecuteCheck("CheckNotebookTabsAndToolbar", () => CheckNotebookTabsAndToolbar(Path.Combine(root, "workspace-navigation")));
            ExecuteCheck("CheckHomePensZoomAndPages", () => CheckHomePensZoomAndPages(Path.Combine(root, "library-workflows")));
            ExecuteCheck("CheckEditingTools", () => CheckEditingTools(Path.Combine(root, "editing-tools")));
            ExecuteCheck("CheckScribbleEditing", () => CheckScribbleEditing(Path.Combine(root, "scribble-editing")));
            ExecuteCheck("CheckEasyScratchOuts", () => CheckEasyScratchOuts(Path.Combine(root, "easy-scratch-outs")));
            ExecuteCheck("CheckPenGestures", () => CheckPenGestures(Path.Combine(root, "pen-gestures")));
            ExecuteCheck("CheckPdfWorkflows", () => CheckPdfWorkflows(Path.Combine(root, "pdf-workflows")));
            ExecuteCheck("CheckPageScrolling", () => CheckPageScrolling(Path.Combine(root, "page-scroll")));
            ExecuteCheck("CheckLibraryActions", () => CheckLibraryActions(Path.Combine(root, "library-actions")));
            ExecuteCheck("CheckColorWheel", () => CheckColorWheel());
            ExecuteCheck("CheckSavedColors", () => CheckSavedColors(Path.Combine(root, "saved-colors")));
            ExecuteCheck("CheckPenRail", () => CheckPenRail(Path.Combine(root, "pen-rail")));
            if (failures.Count > 0) throw new AggregateException("Regression checks failed.", failures);
            Console.WriteLine("PASS: migrations and backups; paper geometry/templates; ink and text history; autosave/recovery; pen-only input/palm suppression; notebook tabs, close-button layout at 100–200% scale, and last-page restoration; toolbar movement and saved placement; Home folders/search/stars; pen presets; touch zoom; independent page duplication and transactional reordering.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // All paths are generated under this test's own directory.
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static Stroke SampleStroke() => new(new StylusPointCollection
    {
        new StylusPoint(80, 96, 0.1f), new StylusPoint(120, 120, 0.8f), new StylusPoint(180, 96, 0.3f)
    }) { DrawingAttributes = new DrawingAttributes { Width = 3, Height = 3, Color = Colors.Blue, FitToCurve = true } };
    private static InkStroke SampleData() => InkMapper.Capture(new StrokeCollection { SampleStroke() }).Strokes[0];
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Await(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            _ = task.ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    private static T Await<T>(Task<T> task) { Await((Task)task); return task.GetAwaiter().GetResult(); }

    private static void CheckRoundTripAndValidation(string root)
    {
        var path = Path.Combine(root, "legacy.stempage");
        var page = InkMapper.Capture(new StrokeCollection { SampleStroke() });
        PageStore.SaveAsync(path, page).GetAwaiter().GetResult();
        var loaded = PageStore.LoadAsync(path).GetAwaiter().GetResult();
        var restored = InkMapper.Capture(InkMapper.Restore(loaded));
        Require(restored.Strokes[0].Id == page.Strokes[0].Id, "Stroke identity lost.");
        Require(restored.Strokes[0].Points.SequenceEqual(page.Strokes[0].Points), "Coordinates or pressure lost.");
        Require(restored.Strokes[0].Color == page.Strokes[0].Color && restored.Strokes[0].Width == 3, "Drawing style lost.");
        PageStore.SaveAsync(path, page with { Strokes = [] }).GetAwaiter().GetResult();
        try
        {
            PageStore.SaveAsync(path, page with { Version = 99 }).GetAwaiter().GetResult();
            throw new InvalidOperationException("Unsupported schema accepted.");
        }
        catch (InvalidDataException) { }
        Require(PageStore.LoadAsync(path).GetAwaiter().GetResult().Strokes.Count == 0, "Failed save damaged the original.");
        File.WriteAllText(path, "{\"Version\":1,\"Width\":816,\"Height\":1056,\"Strokes\":[null]}");
        try
        {
            PageStore.LoadAsync(path).GetAwaiter().GetResult();
            throw new InvalidOperationException("Malformed stroke accepted.");
        }
        catch (InvalidDataException) { }
    }

    private static void CheckHistory()
    {
        var history = new InkHistory();
        var strokes = new StrokeCollection { SampleStroke() };
        history.Record(strokes); strokes.Clear();
        Require(history.Undo().Count == 0, "Undo failed.");
        Require(history.Redo().Count == 1, "History was mutated by live ink.");
        history.Undo();
        history.Record(new StrokeCollection { SampleStroke(), SampleStroke() });
        Require(!history.CanRedo && history.Undo().Count == 0, "New edits did not replace the redo branch.");
        var text = new PageText(Guid.NewGuid(), 20, 30, 200, 20, "#FF24324A", "Energy");
        history.Reset(strokes, [text]); history.Record(strokes, [text with { Text = "Kinetic energy" }]);
        history.Undo(); Require(history.Texts.Single().Text == "Energy", "Text undo was lost.");
        history.Redo(); Require(history.Texts.Single().Text == "Kinetic energy", "Text redo was lost.");
    }

    private static void CheckDatabase(string root)
    {
        Guid last;
        var first = SampleData();
        var second = SampleData();
        var third = SampleData();
        using (var db = new NotebookDatabase(root))
        {
            var initial = db.Snapshot.LastPageId;
            db.ApplyDelta(initial, [new(first, 0), new(second, 1), new(third, 2)], []);
            var fourth = SampleData();
            db.ApplyDelta(initial, [new(fourth, 0)], [second.Id]);
            Require(db.LoadPage(initial).Strokes.Select(s => s.Id).SequenceEqual(new[] { first.Id, third.Id, fourth.Id }),
                "Erasing and adding changed stroke order.");
            var book = new NotebookSummary(Guid.NewGuid(), "Engineering");
            last = Guid.NewGuid();
            db.CreateNotebook(book, last);
            db.RenameNotebook(book.Id, "Physics");
            db.RenamePage(last, "Lecture 1");
            db.ApplyDelta(last, [new(first, 0)], []);
            db.SaveView(last, new PageView(1.8, -100, -250));
            db.SelectPage(last);
            db.MarkClean();
        }
        using (var db = new NotebookDatabase(root))
        {
            Require(!db.Snapshot.RecoveredSession && db.Snapshot.LastPageId == last, "Clean session or selection was lost.");
            Require(db.Snapshot.Notebooks.Any(n => n.Name == "Physics") && db.Snapshot.Pages.Any(p => p.Name == "Lecture 1"), "Names were lost.");
            Require(db.Snapshot.Pages.First(p => p.Id == last).View == new PageView(1.8, -100, -250), "View was not saved.");
            Require(db.LoadPage(last).Strokes[0].Points.SequenceEqual(first.Points), "SQLite changed pressure or positions.");
            try
            {
                using var duplicate = new NotebookDatabase(root);
                throw new InvalidOperationException("A second writer acquired the library lock.");
            }
            catch (IOException) { }
            db.MarkClean();
        }
    }

    private static void CheckAutosave(string root)
    {
        var uiThread = Environment.CurrentManagedThreadId;
        using var writer = new AutosaveWriter(root);
        var snapshot = writer.Initialized.GetAwaiter().GetResult();
        var page = snapshot.LastPageId;
        var attempts = 0;
        var sawError = false;
        writer.Changed += () => { if (writer.Error is not null) sawError = true; };
        writer.Write(db =>
        {
            if (Interlocked.Increment(ref attempts) == 1) throw new IOException("Simulated temporary storage failure.");
            db.RenamePage(page, "Retried page");
        });
        var ink = SampleData();
        writer.Write(db => db.ApplyDelta(page, [new(ink, 0)], []));
        Require(writer.FlushAsync(TimeSpan.FromSeconds(8)).GetAwaiter().GetResult(), "Queued writes did not finish.");
        Require(attempts == 2 && sawError && writer.Error is null && writer.Pending == 0, "Write retry/status is wrong.");
        Require(writer.ReadAsync(_ => Environment.CurrentManagedThreadId != uiThread).GetAwaiter().GetResult(), "Storage ran on the caller thread.");
        Require(writer.ReadAsync(db => db.LoadPage(page).Strokes[0].Id).GetAwaiter().GetResult() == ink.Id, "A later edit was lost after retry.");
        Require(writer.FinishAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), "Clean shutdown failed.");
        writer.StopAsync().GetAwaiter().GetResult();
        using var reopened = new NotebookDatabase(root);
        Require(!reopened.Snapshot.RecoveredSession, "A clean shutdown was marked as a crash.");
        reopened.MarkClean();

        using var blocked = new AutosaveWriter(root + "-blocked");
        _ = blocked.Initialized.GetAwaiter().GetResult();
        var storageBlocked = true;
        blocked.Write(_ => { if (Volatile.Read(ref storageBlocked)) throw new IOException("Storage unavailable."); });
        Require(!blocked.FlushAsync(TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult(), "Blocked storage was reported saved.");
        Require(!blocked.FinishAsync(TimeSpan.FromMilliseconds(150)).GetAwaiter().GetResult(), "Blocked storage allowed a clean shutdown.");
        Volatile.Write(ref storageBlocked, false);
        Require(blocked.FlushAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult(), "Storage did not resume after a canceled close.");
        blocked.StopAsync().GetAwaiter().GetResult();
        using var afterCanceledClose = new NotebookDatabase(root + "-blocked");
        Require(afterCanceledClose.Snapshot.RecoveredSession, "A delayed clean marker hid the interrupted session.");
        afterCanceledClose.MarkClean();
    }

    private static int CrashWriter(string root, string marker)
    {
        using var db = new NotebookDatabase(root);
        var page = db.Snapshot.LastPageId;
        var ink = SampleData();
        db.ApplyDelta(page, [new(ink, 0)], []);
        db.SelectPage(page);
        File.WriteAllText(marker, JsonSerializer.Serialize(new { Page = page, Stroke = ink.Id }));
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }
    private static void CheckCrashRecovery(string root)
    {
        Directory.CreateDirectory(root);
        var marker = Path.Combine(root, "ready.json");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("No process path.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("--crash-writer"); start.ArgumentList.Add(root); start.ArgumentList.Add(marker);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Could not start recovery check.");
        try
        {
            var timer = Stopwatch.StartNew();
            while (!File.Exists(marker) && !child.HasExited && timer.Elapsed < TimeSpan.FromSeconds(15)) Thread.Sleep(50);
            Require(File.Exists(marker), "Recovery child did not commit a page.");
            child.Kill(entireProcessTree: true);
            Require(child.WaitForExit(5000), "Recovery child did not terminate.");
            using var data = JsonDocument.Parse(File.ReadAllText(marker));
            var page = data.RootElement.GetProperty("Page").GetGuid();
            var stroke = data.RootElement.GetProperty("Stroke").GetGuid();
            using var recovered = new NotebookDatabase(root);
            Require(recovered.Snapshot.RecoveredSession, "An interrupted session was not recognized.");
            Require(recovered.Snapshot.LastPageId == page && recovered.LoadPage(page).Strokes.Single().Id == stroke,
                "Committed ink did not survive a killed process.");
            recovered.MarkClean();
        }
        finally { if (!child.HasExited) { child.Kill(entireProcessTree: true); child.WaitForExit(5000); } }
    }

    private sealed class Finger(int id) : TouchDevice(id)
    {
        public Point Position { get; set; }
        public override TouchPoint GetTouchPoint(IInputElement? relativeTo) =>
            new(this, Position, new Rect(Position, new Size(1, 1)), TouchAction.Move);
        public override TouchPointCollection GetIntermediateTouchPoints(IInputElement? relativeTo) => new() { GetTouchPoint(relativeTo) };
        public void Send(UIElement target, RoutedEvent routedEvent) =>
            target.RaiseEvent(new TouchEventArgs(this, Environment.TickCount) { RoutedEvent = routedEvent });
    }

    private static void CheckWindow(string root, string? previewPath)
    {
        var app = new App(); app.InitializeComponent();
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var window = new MainWindow(root);
        Await(window.InitializeLibraryAsync());
        window.ShowWorkspace();
        Require(window.IsReady, "Library UI did not initialize.");
        var first = window.CurrentPageId!.Value;
        var ink = (PenCanvas)window.FindName("InkSurface");
        var viewport = (PageViewport)window.FindName("Viewport");
        Require(!ink.DefaultDrawingAttributes.IgnorePressure && ink.DefaultDrawingAttributes.FitToCurve, "Pen rendering defaults changed.");
        Require(PenCanvas.AcceptsInk(TabletDeviceType.Stylus) && !PenCanvas.AcceptsInk(TabletDeviceType.Touch) && !PenCanvas.AcceptsInk(null),
            "Input policy allows finger or promoted mouse ink.");
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1280, 900)); content.Arrange(new Rect(0, 0, 1280, 900)); content.UpdateLayout();
        viewport.SetView(new PageView(1, 100, 100));
        var finger = new Finger(11) { Position = new Point(200, 200) };
        finger.Send(ink, UIElement.PreviewTouchDownEvent);
        finger.Position = new Point(240, 260); finger.Send(ink, UIElement.PreviewTouchMoveEvent);
        finger.Send(ink, UIElement.PreviewTouchUpEvent);
        Require(viewport.View.X == 140 && viewport.View.Y == 160 && ink.Strokes.Count == 0, "One finger did not pan without drawing.");
        var beforeZoom = viewport.View.Zoom;
        viewport.BeginContact(1, new Point(200, 200)); viewport.BeginContact(2, new Point(400, 200));
        viewport.MoveContact(2, new Point(600, 200)); viewport.EndContact(1); viewport.EndContact(2);
        Require(viewport.View.Zoom > beforeZoom && ink.Strokes.Count == 0, "Pinch did not zoom without drawing.");
        var beforePalm = viewport.View;
        viewport.SetPenPresence(true); viewport.BeginContact(5, new Point(100, 100)); viewport.MoveContact(5, new Point(200, 200));
        Require(viewport.View == beforePalm, "Palm contact moved the page while pen was in range.");
        viewport.SetPenPresence(false);
        viewport.SetView(new PageView(1, 24, 24));
        var mouse = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent };
        ink.RaiseEvent(mouse);
        Require(mouse.Handled && ink.Strokes.Count == 0, "Mouse promotion could reach the ink collector.");

        ink.Strokes.Add(SampleStroke());
        var identity = InkMapper.Capture(ink.Strokes).Strokes[0].Id;
        var originalPoints = InkMapper.Capture(ink.Strokes).Strokes[0].Points.ToArray();
        viewport.BeginContact(31, new Point(100, 150)); viewport.MoveContact(31, new Point(150, 190)); viewport.EndContact(31);
        viewport.Zoom(1.4, new Point(300, 300));
        Require(InkMapper.Capture(ink.Strokes).Strokes[0].Points.SequenceEqual(originalPoints), "Navigation changed ink coordinates or pressure.");
        ((RadioButton)window.FindName("EraserButton")).IsChecked = true;
        Require(ink.EditingMode == InkCanvasEditingMode.EraseByStroke, "Eraser control failed.");
        ((RadioButton)window.FindName("PenButton")).IsChecked = true;
        ((ComboBox)window.FindName("ColorPicker")).SelectedIndex = 2;
        Require(ink.DefaultDrawingAttributes.Color.R == 206 && ink.Strokes[0].DrawingAttributes.Color == Colors.Blue, "Color changes affected existing ink.");
        ((Button)window.FindName("UndoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Strokes.Count == 0, "Window undo failed.");
        ((Button)window.FindName("RedoButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Require(ink.Strokes.Count == 1 && InkMapper.Capture(ink.Strokes).Strokes[0].Id == identity, "Redo lost ink or identity.");
        CheckNewWorkspace(window, ink, viewport, root);
        var second = Await(window.CreatePageAsync());
        Require(ink.Strokes.Count == 0, "A new page contains another page's ink.");
        ink.Strokes.Add(SampleStroke()); ink.Strokes.Add(SampleStroke());
        var third = Await(window.CreateNotebookAsync("Mechanics"));
        Require(ink.Strokes.Count == 0, "A new notebook contains another notebook's ink.");
        for (var i = 0; i < 4; i++) Await(window.CreatePageAsync());
        Await(window.ActivatePageAsync(first));
        Require(ink.Strokes.Count == 1, "Switching notebooks lost the first page.");
        Await(window.ActivatePageAsync(second));
        Require(ink.Strokes.Count == 2, "Switching pages mixed ink.");
        viewport.SetView(new PageView(1.6, -90, -160));
        Require(Await(window.FlushAsync()), "UI autosave did not finish.");
        if (previewPath is not null)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var bitmap = new RenderTargetBitmap(1280, 900, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(previewPath); encoder.Save(stream);
        }
        Require(Await(window.ShutdownLibraryAsync()), "UI shutdown could not save.");
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        Require(reopened.CurrentPageId == second && ((PenCanvas)reopened.FindName("InkSurface")).Strokes.Count == 2, "Restart lost the last open page.");
        Require(((PageViewport)reopened.FindName("Viewport")).View == new PageView(1.6, -90, -160), "Restart lost pan/zoom.");
        Require(((ComboBox)reopened.FindName("NotebookPicker")).Items.Count == 2, "Restart lost a notebook.");
        Await(reopened.ActivatePageAsync(third));
        Require(((PenCanvas)reopened.FindName("InkSurface")).Strokes.Count == 0, "Restart mixed notebooks.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Reopened UI did not close cleanly.");
    }
}
