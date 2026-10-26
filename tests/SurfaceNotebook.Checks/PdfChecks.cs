using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private sealed class ImmediateProgress(Action<string> action) : IProgress<string>
    { public void Report(string value) => action(value); }
    private static void CheckPdfWorkflows(string root)
    {
        Directory.CreateDirectory(root);
        var fixture = Path.Combine(root, "Course handout.pdf");
        using (var pdf = new PdfDocument())
        {
            for (var i = 0; i < 3; i++)
            {
                var page = pdf.AddPage(); page.Width = XUnit.FromPoint(i == 1 ? 792 : 612); page.Height = XUnit.FromPoint(i == 1 ? 612 : 792);
                if (i == 2) { page.Rotate = 90; page.CropBox = new PdfRectangle(new XRect(36, 54, 540, 684)); }
                using var gfx = XGraphics.FromPdfPage(page);
                gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
                gfx.DrawRectangle(XBrushes.AliceBlue, 24, 24, 500, 64);
                gfx.DrawString($"PDF course handout - page {i + 1}", new XFont("Segoe UI", 22), XBrushes.DarkBlue, 40, 64);
                gfx.DrawRectangle(XPens.DarkGreen, 45, 125, 300, 160);
                gfx.DrawLine(new XPen(XColors.OrangeRed, 4), 45, 325, 400, 450);
            }
            pdf.Save(fixture);
        }
        var imported = Await(PdfService.ImportAsync(fixture));
        Require(imported.Count == 3 && imported[0].Width == 816 && imported[0].Height == 1056 && imported[1].Width > imported[1].Height,
            "PDF import lost page count, physical size, or landscape orientation.");
        Require(!imported[0].Background!.RasterExport && imported[2].Background!.RasterExport, $"PDF source/fallback export selection failed: {string.Join(", ", imported.Select(p => p.Background!.RasterExport))}.");
        var portable = Path.Combine(root, "Annotated.stempage");
        Await(PageStore.SaveAsync(portable, imported[0] with { Strokes = [SampleData()] }));
        var restored = Await(PageStore.LoadAsync(portable));
        Require(restored.Version == 3 && restored.Background!.SourcePdf.SequenceEqual(imported[0].Background!.SourcePdf) && restored.Strokes.Count == 1,
            "Portable page round-trip lost the PDF background or ink.");
        var library = Path.Combine(root, "library");
        var window = new MainWindow(library); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        Guid notebook; Guid first; Guid duplicated;
        var output = Path.Combine(root, "Annotated notebook.pdf");
        try
        {
            notebook = Await(window.ImportPdfAsync(fixture)); first = window.CurrentPageId!.Value;
            Require(((System.Windows.Controls.Image)window.FindName("PdfPageImage")).Source is not null && !((Button)window.FindName("PaperButton")).IsEnabled,
                "Imported PDF did not show its locked background.");
            var ink = (PenCanvas)window.FindName("InkSurface"); Collect(ink, SampleStroke());
            duplicated = Await(window.DuplicatePageAsync(first));
            Require(((System.Windows.Controls.Image)window.FindName("PdfPageImage")).Source is not null && ink.Strokes.Count == 1,
                "Duplicating an annotated PDF lost background or ink.");
            Await(window.DeletePageAsync(duplicated));
            Await(window.ActivatePageAsync(first));
            Await(window.ExportPdfAsync(output, entireNotebook: true));
            var single = Path.Combine(root, "Single.pdf"); Await(window.ExportPdfAsync(single, entireNotebook: false));
            using (var verify = PdfReader.Open(single, PdfDocumentOpenMode.Import)) Require(verify.PageCount == 1, "Single-page PDF export included other pages.");
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            try { Await(window.ImportPdfAsync(fixture, canceled.Token)); throw new Exception("Canceled PDF import succeeded."); } catch (OperationCanceledException) { }
            using (var midway = new CancellationTokenSource())
            {
                var progress = new ImmediateProgress(message => { if (message.Contains("page 2 of")) midway.Cancel(); });
                try { Await(window.ImportPdfAsync(fixture, midway.Token, progress)); throw new Exception("Mid-import cancellation succeeded."); } catch (OperationCanceledException) { }
            }
            var invalid = Path.Combine(root, "Invalid.pdf"); File.WriteAllText(invalid, "not a PDF");
            var rejected = false;
            try { Await(window.ImportPdfAsync(invalid)); }
            catch (Exception ex) when (ex is PdfReaderException or InvalidOperationException or InvalidDataException) { rejected = true; }
            Require(rejected, "Invalid PDF import succeeded.");
            Require(window.CurrentPageId == first, "Canceled or invalid import changed the current page.");
            Require(Await(window.ShutdownLibraryAsync()), "PDF library did not save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var exported = Await(PdfService.ImportAsync(output));
        var artifacts = Path.Combine(Environment.CurrentDirectory, "artifacts", "pdf-preview"); Directory.CreateDirectory(artifacts);
        File.Copy(output, Path.Combine(artifacts, "annotated-notebook.pdf"), true);
        File.Copy(fixture, Path.Combine(artifacts, "source-handout.pdf"), true);
        for (var i = 0; i < 3; i++)
        {
            File.WriteAllBytes(Path.Combine(artifacts, $"source-{i + 1}.png"), imported[i].Background!.PreviewPng);
            File.WriteAllBytes(Path.Combine(artifacts, $"export-{i + 1}.png"), exported[i].Background!.PreviewPng);
        }
        Require(exported.Count == 3 && exported.Zip(imported).All(p => Math.Abs(p.First.Width - p.Second.Width) < 1 && Math.Abs(p.First.Height - p.Second.Height) < 1),
            "PDF export changed page order or physical dimensions.");
        for (var i = 0; i < 3; i++)
        {
            var before = new System.Windows.Media.Imaging.FormatConvertedBitmap(PdfService.LoadPreview(imported[i].Background!), System.Windows.Media.PixelFormats.Bgra32, null, 0);
            var after = new System.Windows.Media.Imaging.FormatConvertedBitmap(PdfService.LoadPreview(exported[i].Background!), System.Windows.Media.PixelFormats.Bgra32, null, 0);
            Require(before.PixelWidth == after.PixelWidth && before.PixelHeight == after.PixelHeight, "PDF export changed preview aspect ratio.");
            var stride = before.PixelWidth * 4; var a = new byte[stride * before.PixelHeight]; var b = new byte[a.Length];
            before.CopyPixels(a, stride, 0); after.CopyPixels(b, stride, 0);
            var changed = Enumerable.Range(0, a.Length / 4).Count(p => Math.Abs(a[p * 4] - b[p * 4]) + Math.Abs(a[p * 4 + 1] - b[p * 4 + 1]) + Math.Abs(a[p * 4 + 2] - b[p * 4 + 2]) > 90);
            Require(changed < a.Length / 4 * .08 && (i != 0 || changed > 100), $"PDF rendering moved original content or lost annotations on page {i + 1}: {changed} changed pixels.");
        }
        using (var db = new NotebookDatabase(library))
        {
            Require(db.Snapshot.Notebooks.Count == 2 && db.Snapshot.Pages.Count(p => p.NotebookId == notebook) == 3,
                "Canceled or invalid import left partial notebooks.");
            Require(db.Snapshot.Pages.Single(p => p.Id == first).IsPdf && db.LoadPage(first).Background is not null && db.LoadPage(first).Strokes.Count == 1,
                "PDF background or annotation did not survive restart.");
            var copy = db.DuplicatePage(first, Guid.NewGuid(), "PDF copy");
            Require(copy.Document.Background!.SourcePdf.SequenceEqual(imported[0].Background!.SourcePdf), "Database duplication lost source PDF content.");
            var source = new NotebookCopy(db.Snapshot.Notebooks.Single(n => n.Id == notebook), db.Snapshot.Pages.Where(p => p.NotebookId == notebook)
                .OrderBy(p => p.Position).Select(p => new DuplicatedPage(p, db.LoadPage(p.Id))).ToList());
            var notebookCopy = db.PasteNotebook(source, "PDF notebook copy", null);
            Require(notebookCopy.Pages.All(p => p.Document.Background is not null) && db.LoadPage(notebookCopy.Pages[0].Page.Id).Strokes.Count == 1,
                "Notebook duplication lost PDF backgrounds or annotations.");
            db.DeleteNotebook(notebookCopy.Notebook.Id); db.MarkClean();
        }
        var reopened = new MainWindow(library); Await(reopened.InitializeLibraryAsync()); Await(reopened.ActivatePageAsync(first));
        Require(((System.Windows.Controls.Image)reopened.FindName("PdfPageImage")).Source is not null, "PDF did not render after reopening the library.");
        Require(Await(reopened.ShutdownLibraryAsync()), "Reopened PDF library did not close.");
        var notes = imported[0] with { Background = null, Paper = imported[0].Paper! with { Pattern = PaperPattern.Engineering },
            Strokes = [SampleData()], Texts = [new PageText(Guid.NewGuid(), 100, 200, 500, 26, "#FF24324A", "Force balance: Σ F = mα\nHandwritten notes and typed equations")] };
        var plain = Path.Combine(artifacts, "plain-notes.pdf"); Await(PdfService.ExportAsync(plain, [notes], "Plain notes"));
        var renderedNotes = Await(PdfService.ImportAsync(plain));
        File.WriteAllBytes(Path.Combine(artifacts, "plain-notes.png"), renderedNotes[0].Background!.PreviewPng);
        var previous = File.ReadAllBytes(output);
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { Await(PdfService.ExportAsync(output, imported, "Canceled", canceled.Token)); throw new Exception("Canceled PDF export succeeded."); } catch (OperationCanceledException) { }
        }
        using (var midway = new CancellationTokenSource())
        {
            var progress = new ImmediateProgress(message => { if (message.Contains("page 2 of")) midway.Cancel(); });
            try { Await(PdfService.ExportAsync(output, imported, "Canceled midway", midway.Token, progress)); throw new Exception("Mid-export cancellation succeeded."); } catch (OperationCanceledException) { }
        }
        Require(File.ReadAllBytes(output).SequenceEqual(previous), "Canceled export damaged the existing PDF.");
        var migration = Path.Combine(root, "migration-v032"); Guid legacyPage;
        using (var db = new NotebookDatabase(migration))
        { legacyPage = db.Snapshot.LastPageId; db.ApplyDelta(legacyPage, [new StoredStroke(SampleData(), 0)], []); db.MarkClean(); }
        using (var connection = new SqliteConnection($"Data Source={Path.Combine(migration, "library.db")};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "ALTER TABLE pages DROP COLUMN background_json; PRAGMA user_version=3;"; command.ExecuteNonQuery();
        }
        File.Delete(Path.Combine(migration, "library-v032-backup.db"));
        using (var db = new NotebookDatabase(migration)) { Require(db.LoadPage(legacyPage).Strokes.Count == 1, "Schema 4 migration lost existing ink."); db.MarkClean(); }
        using (var backup = new SqliteConnection($"Data Source={Path.Combine(migration, "library-v032-backup.db")};Mode=ReadOnly;Pooling=False"))
        {
            backup.Open(); using var version = backup.CreateCommand(); version.CommandText = "PRAGMA user_version;";
            Require(Convert.ToInt32(version.ExecuteScalar()) == 3, "The pre-PDF library backup is not schema 3.");
            version.CommandText = "SELECT COUNT(*) FROM strokes;"; Require(Convert.ToInt32(version.ExecuteScalar()) == 1, "The pre-PDF backup lost committed notes.");
        }

    }
}
