using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private Point pageDragStart;
    private Guid? pageDragSource;
    private ListBoxItem? pageDropTarget;
    private const string PageDragFormat = "SurfaceNotebook.PageId";

    private PageSummary? PageFromAction(object sender)
    {
        if (sender is MenuItem item && item.Parent is ContextMenu menu && menu.PlacementTarget is FrameworkElement target &&
            target.DataContext is NavigationPage navigation) return pages.FirstOrDefault(p => p.Id == navigation.Id);
        return pages.FirstOrDefault(p => p.Id == currentPageId);
    }
    public async Task<Guid> DuplicatePageAsync(Guid source)
    {
        if (writer is null || busy) throw new InvalidOperationException("Wait for the current operation to finish.");
        var original = pages.First(p => p.Id == source);
        CommitTextEditor(); SaveCurrentView(); SetBusy(true); model.Status = "Duplicating page…";
        var id = Guid.NewGuid();
        try
        {
            var name = original.Name.Length <= 73 ? original.Name + " (copy)" : original.Name[..73] + " (copy)";
            var copy = await writer.MutateAsync(db => db.DuplicatePage(source, id, name));
            for (var i = 0; i < pages.Count; i++)
                if (pages[i].NotebookId == original.NotebookId && pages[i].Position >= copy.Page.Position)
                    pages[i] = pages[i] with { Position = pages[i].Position + 1 };
            pages.Add(copy.Page);
            sessions[id] = new PageSession(InkMapper.Restore(copy.Document), copy.Document.Paper, copy.Document.Texts, copy.Document.Background);
            if (copy.Page.View is not null) views[id] = copy.Page.View;
            await ActivatePageAsync(id);
            return id;
        }
        finally { SetBusy(false); }
    }
    public async Task MovePageAsync(Guid id, int index)
    {
        if (writer is null || busy) return;
        var page = pages.First(p => p.Id == id);
        var order = pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).Select(p => p.Id).ToList();
        if (index < 0 || index >= order.Count) throw new ArgumentOutOfRangeException(nameof(index));
        var previous = order.IndexOf(id); if (previous == index) return;
        order.Remove(id); order.Insert(index, id);
        SetBusy(true); model.Status = "Reordering pages…";
        try
        {
            await writer.MutateAsync(db => { db.ReorderPages(page.NotebookId, order); return true; });
            for (var i = 0; i < pages.Count; i++)
                if (pages[i].NotebookId == page.NotebookId) pages[i] = pages[i] with { Position = order.IndexOf(pages[i].Id) };
            BindNavigation();
        }
        finally { SetBusy(false); }
    }
    private async void DuplicatePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageFromAction(sender) is not { } page) return;
        try { await DuplicatePageAsync(page.Id); } catch (Exception ex) { ShowError(ex); }
    }
    private async void MovePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageFromAction(sender) is not { } page || sender is not MenuItem item) return;
        var ordered = pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).ToList();
        var target = ordered.FindIndex(p => p.Id == page.Id) + ((string)item.Tag == "earlier" ? -1 : 1);
        if (target < 0 || target >= ordered.Count) return;
        try { await MovePageAsync(page.Id, target); } catch (Exception ex) { ShowError(ex); }
    }
    private void PageMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        PageSummary? page = menu.PlacementTarget is FrameworkElement target && target.DataContext is NavigationPage navigation ?
            pages.FirstOrDefault(p => p.Id == navigation.Id) : pages.FirstOrDefault(p => p.Id == currentPageId);
        if (page is null) return;
        var ordered = pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).ToList();
        foreach (var item in menu.Items.OfType<MenuItem>())
        {
            if (item.Tag as string == "earlier") item.IsEnabled = !busy && ordered[0].Id != page.Id;
            else if (item.Tag as string == "later") item.IsEnabled = !busy && ordered[^1].Id != page.Id;
            else item.IsEnabled = !busy;
        }
    }
    private void PageDrag_Start(object sender, MouseButtonEventArgs e)
    {
        if (busy || e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch || sender is not FrameworkElement { DataContext: NavigationPage page }) return;
        pageDragStart = e.GetPosition(PageList); pageDragSource = page.Id; e.Handled = true;
    }
    private void PageDrag_Move(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed) { pageDragSource = null; return; }
        if (pageDragSource is not Guid id || busy || e.LeftButton != MouseButtonState.Pressed || e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch) return;
        var delta = e.GetPosition(PageList) - pageDragStart;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        pageDragSource = null;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(PageDragFormat, id), DragDropEffects.Move); }
        finally { ClearPageDropTarget(); }
        e.Handled = true;
    }
    private async void PageDrag_End(object sender, MouseButtonEventArgs e)
    {
        var id = pageDragSource; pageDragSource = null;
        if (id is null || busy || e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch) return;
        e.Handled = true;
        try { await ActivatePageAsync(id.Value); } catch (Exception ex) { ShowError(ex); }
    }
    private ListBoxItem? PageItemAt(DependencyObject? node)
    {
        while (node is not null && node != PageList)
        {
            if (node is ListBoxItem item) return item;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }
    private void PageList_DragOver(object sender, DragEventArgs e)
    {
        ClearPageDropTarget();
        if (!busy && e.Data.GetDataPresent(PageDragFormat) && PageItemAt(e.OriginalSource as DependencyObject) is { } target)
        {
            pageDropTarget = target; target.Opacity = .6; e.Effects = DragDropEffects.Move;
        }
        else e.Effects = DragDropEffects.None;
        e.Handled = true;
    }
    private async void PageList_Drop(object sender, DragEventArgs e)
    {
        ClearPageDropTarget(); e.Handled = true;
        if (busy || e.Data.GetData(PageDragFormat) is not Guid id || PageItemAt(e.OriginalSource as DependencyObject)?.DataContext is not NavigationPage target) return;
        var source = pages.FirstOrDefault(p => p.Id == id); if (source is null || source.NotebookId != target.Page.NotebookId) return;
        var ordered = pages.Where(p => p.NotebookId == source.NotebookId).OrderBy(p => p.Position).ToList();
        try { await MovePageAsync(id, ordered.FindIndex(p => p.Id == target.Id)); } catch (Exception ex) { ShowError(ex); }
    }
    private void PageList_DragLeave(object sender, DragEventArgs e) => ClearPageDropTarget();
    private void ClearPageDropTarget() { if (pageDropTarget is not null) pageDropTarget.Opacity = 1; pageDropTarget = null; }
}
