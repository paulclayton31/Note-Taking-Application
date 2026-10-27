using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static Stroke RoundedScratch(double height, int samples = 20)
    {
        var radius = height / 10;
        var points = new List<StylusPoint> { new StylusPoint(0, height - radius), new StylusPoint(0, radius) };
        for (var i = 1; i <= samples; i++)
        {
            var angle = Math.PI + Math.PI * i / samples;
            points.Add(new StylusPoint(radius + radius * Math.Cos(angle), radius + radius * Math.Sin(angle)));
        }
        points.Add(new StylusPoint(radius * 2, height - radius));
        for (var i = 1; i <= samples; i++)
        {
            var angle = Math.PI - Math.PI * i / samples;
            points.Add(new StylusPoint(radius * 3 + radius * Math.Cos(angle), height - radius + radius * Math.Sin(angle)));
        }
        points.Add(new StylusPoint(radius * 4, height / 2));
        return new Stroke(new StylusPointCollection(points));
    }
    private static Stroke UnevenScratch() => new(new StylusPointCollection
    { new StylusPoint(120, 82), new StylusPoint(121, 135), new StylusPoint(123, 82), new StylusPoint(124, 94) });
    private static void CheckEasyScratchOuts(string root)
    {
        foreach (var height in new[] { 13d, 24, 100 })
            foreach (var samples in new[] { 6, 40 })
                foreach (var angle in new[] { 0d, .65, Math.PI / 2 })
                {
                    var rounded = RoundedScratch(height, samples);
                    rounded.Transform(new Matrix(Math.Cos(angle), Math.Sin(angle), -Math.Sin(angle), Math.Cos(angle), 0, 0), false);
                    Require(ScribbleGesture.IsScribble(rounded), $"A rounded scratch was rejected at height {height}, angle {angle}, sampling {samples}.");
                }
        Require(ScribbleGesture.IsScribble(UnevenScratch()), "A short finishing pass still required a full-length third pass.");
        var containedTurns = new Stroke(new StylusPointCollection { new StylusPoint(0, 60), new StylusPoint(0, 20), new StylusPoint(3, 80), new StylusPoint(4, -10) });
        Require(ScribbleGesture.Analyze(containedTurns)?.CrossesRepeatedly(new Rect(-10, 0, 30, 100)) == true,
            "Passes inside the target were ignored when the final tip left its bounds.");
        foreach (var sign in new[] { -1, 1 })
        {
            var oval = new Stroke(new StylusPointCollection(Enumerable.Range(0, 240).Select(i =>
                new StylusPoint(120 + 5 * Math.Cos(i * Math.PI / 40), 110 + 35 * Math.Sin(sign * i * Math.PI / 40)))));
            Require(!ScribbleGesture.IsScribble(oval), "Repeated narrow ovals became an erase gesture.");
        }
        var letterW = new Stroke(new StylusPointCollection { new StylusPoint(0, 0), new StylusPoint(12, 30), new StylusPoint(24, 0), new StylusPoint(36, 30), new StylusPoint(48, 0) });
        var singleReturn = new Stroke(new StylusPointCollection { new StylusPoint(0, 0), new StylusPoint(0, 20), new StylusPoint(0, 40), new StylusPoint(4, 0) });
        Require(!ScribbleGesture.IsScribble(letterW) && !ScribbleGesture.IsScribble(singleReturn), "A letter or single return became an erase gesture.");
        Guid first, densePage;
        using (var db = new NotebookDatabase(root))
        {
            first = db.Snapshot.LastPageId; var book = db.Snapshot.Notebooks[0].Id;
            var far = SampleStroke(); far.Transform(new Matrix(1, 0, 0, 1, 400, 400), false);
            var diagram = new Stroke(new StylusPointCollection { new StylusPoint(10, 110), new StylusPoint(700, 110) });
            db.ApplyDelta(first, InkMapper.Capture(new StrokeCollection { SampleStroke(), far, diagram }).Strokes.Select((s, i) => new StoredStroke(s, i)).ToArray(), []);
            densePage = Guid.NewGuid(); db.CreatePage(new PageSummary(densePage, book, "Connected handwriting", 1, null));
            var dense = new Stroke(new StylusPointCollection(Enumerable.Range(0, 1000).Select(i =>
                new StylusPoint(120 + 35 * Math.Cos(i * Math.PI / 10), 110 + 15 * Math.Sin(i * Math.PI / 10))).Append(new StylusPoint(250, 110))));
            db.ApplyDelta(densePage, InkMapper.Capture(new StrokeCollection { dense, far }).Strokes.Select((s, i) => new StoredStroke(s, i)).ToArray(), []);
            db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        try
        {
            var ink = (PenCanvas)window.FindName("InkSurface"); var ids = ink.Strokes.Select(InkMapper.Identity).ToArray();
            var rounded = RoundedScratch(100); rounded.Transform(new Matrix(1, 0, 0, 1, 110, 65), false);
            var shortMark = new Stroke(new StylusPointCollection { new StylusPoint(118, 104), new StylusPoint(119, 124), new StylusPoint(121, 104), new StylusPoint(122, 119) });
            foreach (var mark in new[] { rounded, UnevenScratch(), shortMark })
            {
                Collect(ink, mark);
                Require(ink.Strokes.Select(InkMapper.Identity).SequenceEqual(ids.Skip(1)), "An easy scratch missed handwriting or deleted a distant/long diagram stroke.");
                Click((Button)window.FindName("UndoButton"));
                Require(ink.Strokes.Count == 4 && ink.Strokes.Any(s => InkMapper.Identity(s) == ids[0]), "Undo did not restore an easy scratch and its erased ink.");
                Click((Button)window.FindName("RedoButton")); Require(ink.Strokes.Count == 2, "An easy scratch could not redo.");
                Click((Button)window.FindName("UndoButton")); Click((Button)window.FindName("UndoButton"));
                Require(ink.Strokes.Select(InkMapper.Identity).SequenceEqual(ids), "Scratch undo changed the original strokes.");
            }
            var gap = new Stroke(new StylusPointCollection { new StylusPoint(130, 75), new StylusPoint(131, 98), new StylusPoint(133, 75), new StylusPoint(134, 95) });
            Collect(ink, gap);
            Require(ink.Strokes.Count == 4, "A scratch in a gap erased ink it did not touch.");
            Click((Button)window.FindName("UndoButton"));
            Await(window.ActivatePageAsync(densePage));
            var denseIds = ink.Strokes.Select(InkMapper.Identity).ToArray();
            var narrow = new Stroke(new StylusPointCollection { new StylusPoint(230, 88), new StylusPoint(231, 132), new StylusPoint(232, 88), new StylusPoint(233, 126) });
            Collect(ink, narrow);
            Require(ink.Strokes.Count == 1 && InkMapper.Identity(ink.Strokes[0]) == denseIds[1], "Whole-stroke percentage coverage still blocked a narrow scratch over connected ink.");
            Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "Easy scratch-outs did not save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        Require(reopened.CurrentPageId == densePage && ((PenCanvas)reopened.FindName("InkSurface")).Strokes.Count == 1, "An easy scratch-out did not survive restart.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Reopened scratch-out library did not close.");
    }
}
