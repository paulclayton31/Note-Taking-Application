using System.IO;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

// Deterministic screenshots from the actual WPF application, using an isolated
// sample library. This path never opens or modifies the user's notebook library.
public static class PreviewCapture
{
    public static async Task GenerateAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var temporary = Path.Combine(Path.GetTempPath(), "SurfaceNotebook-preview-" + Guid.NewGuid().ToString("N"));
        MainWindow? window = null;
        try
        {
            Seed(temporary);
            window = new MainWindow(temporary);
            await window.InitializeLibraryAsync();
            window.ShowHome();
            await CaptureAsync(window, Path.Combine(outputDirectory, "home-light.png"), 1440, 1000);
            window.SetDarkTheme(true);
            await CaptureAsync(window, Path.Combine(outputDirectory, "home-dark.png"), 1440, 1000);
            window.SetDarkTheme(false);
            window.ShowWorkspace();
            await CaptureAsync(window, Path.Combine(outputDirectory, "workspace-light.png"), 1440, 1000);
            window.ShowToolInspector();
            await CaptureAsync(window, Path.Combine(outputDirectory, "tool-inspector.png"), 1440, 1000);
            await CaptureAsync(window, Path.Combine(outputDirectory, "tool-inspector-portrait.png"), 960, 1440);
            window.CloseToolInspector();
            window.SetDarkTheme(true);
            await CaptureAsync(window, Path.Combine(outputDirectory, "workspace-dark.png"), 1440, 1000);
            window.SetDarkTheme(false); window.ShowPaperPreview();
            await CaptureAsync(window, Path.Combine(outputDirectory, "paper-settings.png"), 1440, 1000);
            ((FrameworkElement)window.FindName("ContextPanel")).Visibility = Visibility.Collapsed;
            window.ToggleFocus();
            await CaptureAsync(window, Path.Combine(outputDirectory, "focus-mode.png"), 1440, 960);
            window.ToggleFocus();
            await CaptureAsync(window, Path.Combine(outputDirectory, "portrait.png"), 960, 1440);
            await CaptureAsync(window, Path.Combine(outputDirectory, "compact-window.png"), 720, 700);
            if (!await window.ShutdownLibraryAsync()) throw new IOException("Preview library did not flush.");
            window = null;
        }
        finally
        {
            if (window is not null) await window.ShutdownLibraryAsync();
            if (Path.GetFullPath(temporary).StartsWith(Path.Combine(Path.GetTempPath(), "SurfaceNotebook-preview-"), StringComparison.OrdinalIgnoreCase))
                try { Directory.Delete(temporary, recursive: true); } catch (IOException) { }
        }
    }
    public static async Task CaptureAsync(MainWindow window, string file, int width, int height)
    {
        window.UpdateResponsiveLayout(width);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        ((Controls.PageViewport)window.FindName("Viewport")).FitPage();
        content.UpdateLayout();
        await Dispatcher.Yield(DispatcherPriority.Render);
        content.UpdateLayout();
        var image = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); image.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(file); png.Save(stream);
    }
    private static void Seed(string directory)
    {
        using var db = new NotebookDatabase(directory);
        var first = db.Snapshot.LastPageId; var book = db.Snapshot.Notebooks[0];
        db.RenameNotebook(book.Id, "Physics II");
        db.RenamePage(first, "Oscillations & energy");
        var paper = PaperSettings.Default with { Pattern = PaperPattern.Graph, Spacing = 24, LineColor = "#FFE6EAF0" };
        db.SaveNotebookPaper(book.Id, paper);
        var ink = new StrokeCollection();
        void Line(IEnumerable<Point> points, string color = "#FF354C75", double width = 2.3)
        {
            var path = points.ToArray();
            ink.Add(new Stroke(new StylusPointCollection(path.Select((p, i) => new StylusPoint(p.X, p.Y, (float)(.5 + .15 * Math.Sin(i * .28))))))
            { DrawingAttributes = new DrawingAttributes { Width = width, Height = width, Color = (Color)ColorConverter.ConvertFromString(color), FitToCurve = path.Length > 30 } });
        }
        // A hand-drawn spring, mass, and displacement graph, all ordinary editable ink.
        Line([new(95, 222), new(95, 338)], "#FF6F7A90", 3);
        for (var i = 0; i < 6; i++) Line([new(77, 230 + i * 18), new(95, 222 + i * 18)], "#FF8B94A4", 1.4);
        Line([new(95, 280), new(119, 280), new(130, 267), new(145, 294), new(160, 267), new(175, 294),
            new(190, 267), new(205, 294), new(220, 267), new(235, 280), new(258, 280)]);
        Line([new(258, 246), new(332, 246), new(332, 314), new(258, 314), new(258, 246)]);
        Line([new(72, 324), new(368, 324)], "#FFA8B0BE", 1.4);
        Line([new(284, 354), new(358, 354), new(349, 348)], "#FF655BBD");
        Line([new(286, 354), new(286, 338)], "#FF655BBD");
        Line([new(462, 328), new(462, 208), new(457, 217)], "#FF8B94A4", 1.4);
        Line([new(450, 280), new(742, 280), new(734, 275)], "#FF8B94A4", 1.4);
        Line(Enumerable.Range(0, 160).Select(i => new Point(474 + i * 1.6, 280 - 48 * Math.Cos(i * Math.PI / 50))), "#FF7063BB", 2.7);
        Line([new(65, 468), new(714, 468)], "#FFE7B944", 14);
        ink[^1].DrawingAttributes.IsHighlighter = true;
        ink[^1].DrawingAttributes.StylusTip = StylusTip.Rectangle;
        var texts = new List<PageText>();
        void Text(double x, double y, string text, double size = 18, string color = "#FF35415A", double width = 680) =>
            texts.Add(new PageText(Guid.NewGuid(), x, y, width, size, color, text));
        Text(64, 50, "PHYSICS II     /     LECTURE 06", 12, "#FF8B829E");
        Text(64, 81, "Oscillations & energy", 32, "#FF26344C");
        Text(64, 139, "A simple model for motion that repeats.", 18, "#FF6D788C");
        Text(278, 264, "m", 20, width: 50);
        Text(294, 354, "x", 16, "#FF655BBD", 40);
        Text(464, 191, "x(t)", 15, "#FF6D788C", 60); Text(735, 281, "t", 15, "#FF6D788C", 40);
        Text(65, 403, "01   The restoring force", 20, "#FF26344C");
        Text(65, 451, "F = −kx        →        m x″ + kx = 0", 25);
        Text(65, 507, "Displacement and force point in opposite directions.\nThe spring pulls the mass back toward equilibrium.", 18);
        Text(65, 606, "02   A solution that keeps coming back", 20, "#FF26344C");
        Text(65, 652, "x(t) = A cos(ωt + φ)        ω = √(k / m)", 25, "#FF655BBD");
        Text(65, 714, "A   amplitude       φ   initial phase       T = 2π / ω", 18);
        Text(65, 817, "TO THINK ABOUT", 12, "#FF8B829E");
        Text(65, 853, "How would doubling the mass change the period?\nTry predicting it before calculating.", 18, "#FF6D788C");
        db.ReplacePage(first, InkMapper.Capture(ink) with { Width = paper.Width, Height = paper.Height, Paper = paper, Texts = texts });
        var second = new PageSummary(Guid.NewGuid(), book.Id, "Worked examples", 1, null, paper);
        db.CreatePage(second); db.SaveTexts(second.Id, [new PageText(Guid.NewGuid(), 64, 80, 660, 30, "#FF26344C", "Worked examples")]);
        var third = new PageSummary(Guid.NewGuid(), book.Id, "Questions for Friday", 2, null, paper with { Pattern = PaperPattern.Dots });
        db.CreatePage(third);
        var calculus = new NotebookSummary(Guid.NewGuid(), "Calculus");
        var calculusPage = Guid.NewGuid();
        db.CreateNotebook(calculus, calculusPage);
        var algebra = new NotebookSummary(Guid.NewGuid(), "Linear algebra");
        db.CreateNotebook(algebra, Guid.NewGuid());
        var journal = new NotebookSummary(Guid.NewGuid(), "Lab journal");
        db.CreateNotebook(journal, Guid.NewGuid());
        var semester = new FolderSummary(Guid.NewGuid(), "Fall semester");
        db.CreateFolder(semester); db.CreateFolder(new FolderSummary(Guid.NewGuid(), "Reference & reading"));
        db.CreateFolder(new FolderSummary(Guid.NewGuid(), "Engineering", semester.Id));
        db.FavoriteNotebook(book.Id, true);
        db.SavePreferences(new WorkspacePreferences(Favorites: [first], OpenNotebooks: [book.Id, calculus.Id],
            NotebookPages: new Dictionary<Guid, Guid> { [book.Id] = first, [calculus.Id] = calculusPage }, HomeVisible: true));
        db.SelectPage(first); db.MarkClean();
    }
}
