using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using SurfaceNotebook.Models;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private NotebookCopy? notebookClipboard;
    public bool CanPasteNotebook => notebookClipboard is not null;

    private void ClearActivePage()
    {
        viewTimer.Stop(); thumbnailTimer.Stop(); thumbnailGeneration++;
        currentPageId = null; session = null;
        SetStrokes(new StrokeCollection()); RenderTexts();
        bindingSelection = true;
        try { PageList.ItemsSource = null; NotebookPicker.SelectedItem = null; DocumentTabs.SelectedItem = null; }
        finally { bindingSelection = false; }
        RefreshHistory(); UpdateSelectionActions();
    }
    private void ForgetPage(Guid id)
    {
        sessions.Remove(id); views.Remove(id); thumbnails.Remove(id); navigationPages.Remove(id); favorites.Remove(id);
        foreach (var book in notebookPages.Where(p => p.Value == id).Select(p => p.Key).ToList()) notebookPages.Remove(book);
    }
    private async Task<NotebookCopy> CaptureNotebookAsync(Guid id)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        var book = notebooks.First(n => n.Id == id);
        CommitTextEditor(); SaveCurrentView();
        var ordered = pages.Where(p => p.NotebookId == id).OrderBy(p => p.Position)
            .Select(p => views.TryGetValue(p.Id, out var view) ? p with { View = view } : p).ToList();
        SetBusy(true);
        try { return await writer.ReadAsync(db => new NotebookCopy(book, ordered.Select(p => new DuplicatedPage(p, db.LoadPage(p.Id))).ToList())); }
        finally { SetBusy(false); }
    }
    public async Task CopyNotebookAsync(Guid id)
    {
        notebookClipboard = await CaptureNotebookAsync(id);
        HomeView.SetPasteAvailable(true); model.Status = "Notebook copied · choose a folder and Paste notebook";
    }
    public async Task<Guid> DuplicateNotebookAsync(Guid id)
    {
        var source = await CaptureNotebookAsync(id);
        var copy = await PasteNotebookCopyAsync(source, source.Notebook.FolderId);
        if (!homeVisible) await ActivateNotebookAsync(copy);
        return copy;
    }
    public Task<Guid> PasteNotebookAsync(Guid? folder = null) => PasteNotebookCopyAsync(
        notebookClipboard ?? throw new InvalidOperationException("Copy a notebook first."), folder);
    private async Task<Guid> PasteNotebookCopyAsync(NotebookCopy source, Guid? folder)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        if (folder is Guid target && folders.All(f => f.Id != target)) throw new ArgumentException("Choose an existing folder.");
        var stem = source.Notebook.Name[..Math.Min(65, source.Notebook.Name.Length)];
        var name = stem + " (copy)"; var suffix = 2;
        while (notebooks.Any(n => n.FolderId == folder && n.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) name = $"{stem} (copy {suffix++})";
        SetBusy(true);
        try
        {
            var copy = await writer.MutateAsync(db => db.PasteNotebook(source, name, folder));
            bindingSelection = true;
            try { notebooks.Add(copy.Notebook); } finally { bindingSelection = false; }
            pages.AddRange(copy.Pages.Select(p => p.Page));
            foreach (var page in copy.Pages) if (page.Page.View is not null) views[page.Page.Id] = page.Page.View;
            RefreshHome(); model.Status = "Notebook copied"; return copy.Notebook.Id;
        }
        finally { SetBusy(false); }
    }
    public async Task DeletePageAsync(Guid id)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        var page = pages.First(p => p.Id == id);
        var siblings = pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).ToList();
        var index = siblings.IndexOf(page); siblings.Remove(page);
        CommitTextEditor(); SaveCurrentView(); SetBusy(true);
        try
        {
            await writer.MutateAsync(db => { db.DeletePage(id); return true; });
            pages.Remove(page); ForgetPage(id);
            for (var i = 0; i < pages.Count; i++) if (pages[i].NotebookId == page.NotebookId && pages[i].Position > page.Position)
                pages[i] = pages[i] with { Position = pages[i].Position - 1 };
            if (currentPageId == id)
            {
                ClearActivePage(); SetBusy(false); ShowHome();
                if (siblings.Count > 0) await ActivatePageAsync(siblings[Math.Min(index, siblings.Count - 1)].Id);
                else
                {
                    bindingSelection = true;
                    try { var tab = openNotebooks.FirstOrDefault(n => n.Id == page.NotebookId); if (tab is not null) openNotebooks.Remove(tab); }
                    finally { bindingSelection = false; }
                    SetBusy(false); ShowHome();
                }
            }
            BindNavigation(); SavePreferences(); RefreshHome();
        }
        finally { SetBusy(false); }
    }
    public async Task DeleteNotebookAsync(Guid id)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        var book = notebooks.First(n => n.Id == id);
        var removed = pages.Where(p => p.NotebookId == id).Select(p => p.Id).ToList();
        CommitTextEditor(); SaveCurrentView(); SetBusy(true);
        try
        {
            await writer.MutateAsync(db => { db.DeleteNotebook(id); return true; });
            var active = removed.Contains(currentPageId ?? Guid.Empty);
            if (active) ClearActivePage();
            bindingSelection = true;
            try
            {
                var tab = openNotebooks.FirstOrDefault(n => n.Id == id); if (tab is not null) openNotebooks.Remove(tab);
                notebooks.Remove(book);
            }
            finally { bindingSelection = false; }
            pages.RemoveAll(p => p.NotebookId == id); removed.ForEach(ForgetPage); notebookPages.Remove(id);
            SetBusy(false);
            if (active || currentPageId is null) ShowHome();
            BindNavigation(); RefreshHome(); SavePreferences();
        }
        finally { SetBusy(false); }
    }
    private async Task ConfirmDeleteNotebookAsync(Guid id)
    {
        var book = notebooks.First(n => n.Id == id); var count = pages.Count(p => p.NotebookId == id);
        if (MessageBox.Show(this, $"Delete “{book.Name}” and its {count} page(s)?\n\nThis permanently deletes their handwriting and text. This cannot be undone.",
            "Delete notebook?", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes) await DeleteNotebookAsync(id);
    }
    private async void DeletePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageFromAction(sender) is not { } page || busy) return;
        if (MessageBox.Show(this, $"Permanently delete “{page.Name}” and its handwriting and text?\n\nThis cannot be undone.", "Delete page?",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        try { await DeletePageAsync(page.Id); } catch (Exception ex) { ShowError(ex); }
    }
    private async void NotebookAction_Click(object sender, RoutedEventArgs e)
    {
        if (NotebookPicker.SelectedItem is not NotebookSummary book || sender is not MenuItem item || busy) return;
        try
        {
            switch (item.Tag as string)
            {
                case "copy": await CopyNotebookAsync(book.Id); break;
                case "duplicate": await DuplicateNotebookAsync(book.Id); break;
                case "delete": await ConfirmDeleteNotebookAsync(book.Id); break;
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
