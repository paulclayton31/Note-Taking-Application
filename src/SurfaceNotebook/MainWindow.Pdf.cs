using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    public async Task<Guid> ImportPdfAsync(string path, CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        var folder = homeVisible ? homeFolder : (NotebookPicker.SelectedItem as NotebookSummary)?.FolderId;
        CommitTextEditor(); SaveCurrentView(); SetBusy(true);
        try
        {
            var imported = await PdfService.ImportAsync(path, cancellation, progress);
            cancellation.ThrowIfCancellationRequested(); progress?.Report("Saving PDF notebook…");
            var name = Path.GetFileNameWithoutExtension(path); if (string.IsNullOrWhiteSpace(name)) name = "Imported PDF";
            name = name[..Math.Min(70, name.Length)]; var stem = name; var suffix = 2;
            while (notebooks.Any(n => n.FolderId == folder && n.Name == name)) name = $"{stem} {suffix++}";
            var book = new NotebookSummary(Guid.NewGuid(), name, imported[0].Paper, folder);
            var source = new NotebookCopy(book, imported.Select((doc, index) => new DuplicatedPage(
                new PageSummary(Guid.NewGuid(), book.Id, $"Page {index + 1}", index, null, doc.Paper, IsPdf: true), doc)).ToList());
            var copy = await writer.MutateAsync(db => db.PasteNotebook(source, name, folder));
            bindingSelection = true;
            try { notebooks.Add(copy.Notebook); } finally { bindingSelection = false; }
            pages.AddRange(copy.Pages.Select(p => p.Page));
            await ActivateNotebookAsync(copy.Notebook.Id); return copy.Notebook.Id;
        }
        finally { SetBusy(false); RefreshHome(); }
    }
    public async Task ExportPdfAsync(string path, bool entireNotebook, CancellationToken cancellation = default, IProgress<string>? progress = null, Guid? pageId = null)
    {
        var selected = pageId ?? currentPageId;
        if (writer is null || busy || selected is not Guid id) throw new InvalidOperationException("Open a notebook to export it.");
        CommitTextEditor(); SaveCurrentView();
        var page = pages.First(p => p.Id == id); var title = entireNotebook ? notebooks.First(n => n.Id == page.NotebookId).Name : page.Name;
        var ids = entireNotebook ? pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).Select(p => p.Id).ToList() : [id];
        SetBusy(true);
        try
        {
            var documents = await writer.ReadAsync(db => ids.Select(db.LoadPage).ToList());
            await PdfService.ExportAsync(path, documents, title, cancellation, progress);
        }
        finally { SetBusy(false); }
    }
    private void ImportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Title = "Import PDF as notebook", Filter = "PDF documents (*.pdf)|*.pdf", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        try { PdfOperationDialog.Run(this, "Import PDF", (token, progress) => ImportPdfAsync(dialog.FileName, token, progress)); }
        catch (OperationCanceledException) { model.Status = "PDF import canceled"; }
        catch (Exception ex) { ShowError(new InvalidOperationException("Could not import the PDF. Your existing notes are unchanged.\n\n" + ex.Message, ex)); }
    }
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (busy || currentPageId is not Guid id) return;
        var entire = sender is MenuItem { Tag: "notebook" };
        var page = entire ? pages.First(p => p.Id == id) : PageFromAction(sender) ?? pages.First(p => p.Id == id);
        var dialog = new SaveFileDialog { Title = entire ? "Export notebook as PDF" : "Export page as PDF", Filter = "PDF document (*.pdf)|*.pdf",
            DefaultExt = ".pdf", FileName = entire ? notebooks.First(n => n.Id == page.NotebookId).Name : page.Name };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            PdfOperationDialog.Run(this, "Export PDF", async (token, progress) => { await ExportPdfAsync(dialog.FileName, entire, token, progress, page.Id); return true; });
            model.Status = "PDF exported";
        }
        catch (OperationCanceledException) { model.Status = "PDF export canceled"; }
        catch (Exception ex) { ShowError(ex); }
    }
}
