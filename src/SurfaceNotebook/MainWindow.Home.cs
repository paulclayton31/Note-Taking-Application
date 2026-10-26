using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly List<FolderSummary> folders = [];
    private bool homeVisible;
    private Guid? homeFolder;
    private string homeSection = "library";
    private int homeCoverGeneration;
    public bool IsHomeVisible => homeVisible;
    public IReadOnlyList<FolderSummary> Folders => folders.AsReadOnly();
    private void InitializeHome()
    {
        HomeView.NotebookOpened += async id => { try { await ActivateNotebookAsync(id); } catch (Exception ex) { ShowError(ex); } };
        HomeView.FolderOpened += id => { homeFolder = id; homeSection = "library"; HomeView.ClearSearch(); RefreshHome(); };
        HomeView.SectionChanged += section => { homeSection = section; homeFolder = null; HomeView.ClearSearch(); RefreshHome(); };
        HomeView.FilterChanged += RefreshHome;
        HomeView.NewNotebookRequested += () => AddNotebook_Click(this, new RoutedEventArgs());
        HomeView.NewFolderRequested += async () =>
        {
            var name = NameDialog.Ask(this, "New folder", "New folder");
            if (name is not null) try { await CreateFolderAsync(name, homeFolder); } catch (Exception ex) { ShowError(ex); }
        };
        HomeView.ResumeRequested += ShowWorkspace;
        HomeView.PasteRequested += async () =>
        {
            try { await PasteNotebookAsync(homeFolder); } catch (Exception ex) { ShowError(ex); }
        };
        HomeView.ActionRequested += HomeAction;
    }
    private void Home_Click(object sender, RoutedEventArgs e) => ShowHome();
    public void ShowHome()
    {
        if (!ready || busy) return;
        if (focusMode) ToggleFocus();
        CommitTextEditor(); SaveCurrentView();
        if (currentPageId is Guid id && session is not null) UpdateThumbnail(id, CaptureCurrent());
        homeVisible = true;
        CloseToolInspector(); ZoomPopup.IsOpen = false; selectionMenu.IsOpen = false;
        EditNotice.Visibility = Visibility.Collapsed; editNoticeTimer.Stop(); UpdateSelectionActions();
        EditorRoot.Visibility = Visibility.Collapsed; HomeView.Visibility = Visibility.Visible;
        FocusButton.IsEnabled = false;
        HomeButton.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "Selected");
        bindingSelection = true; DocumentTabs.SelectedItem = null; bindingSelection = false;
        RefreshHome(); SavePreferences();
    }
    public void ShowWorkspace()
    {
        if (currentPageId is null) { ShowHome(); return; }
        homeVisible = false; homeCoverGeneration++;
        HomeView.Visibility = Visibility.Collapsed; EditorRoot.Visibility = Visibility.Visible;
        FocusButton.IsEnabled = true; HomeButton.Background = Brushes.Transparent;
        if (currentPageId is not null) BindNavigation();
        UpdateSelectionActions();
        SavePreferences();
    }
    private string FolderPath(Guid? id)
    {
        var parts = new List<string>(); var visited = new HashSet<Guid>();
        while (id is Guid value && visited.Add(value) && folders.FirstOrDefault(f => f.Id == value) is { } folder)
        { parts.Insert(0, folder.Name); id = folder.ParentId; }
        return parts.Count == 0 ? "Library" : string.Join(" / ", parts);
    }
    private List<HomeFolderItem> FolderNavigationItems()
    {
        var result = new List<HomeFolderItem>(); var visited = new HashSet<Guid>();
        void Visit(Guid? parent, int depth)
        {
            foreach (var folder in folders.Where(f => f.ParentId == parent).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                if (!visited.Add(folder.Id)) continue;
                result.Add(new HomeFolderItem(folder.Id, folder.Name, "", Math.Min(depth, 5))); Visit(folder.Id, depth + 1);
            }
        }
        Visit(null, 0); return result;
    }
    private void RefreshHome()
    {
        if (!ready || HomeView is null || !homeVisible) return;
        var query = HomeView.Query;
        var filtered = notebooks.Where(n => query.Length > 0
            ? (n.Name + " " + FolderPath(n.FolderId)).Contains(query, StringComparison.OrdinalIgnoreCase)
            : homeSection == "starred" ? n.Favorite : homeSection == "recent" ? n.LastOpened > 0 : n.FolderId == homeFolder);
        filtered = HomeView.SortByName ? filtered.OrderBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase)
            : filtered.OrderByDescending(n => n.LastOpened).ThenBy(n => n.Name, StringComparer.CurrentCultureIgnoreCase);
        var visibleBooks = filtered.ToList();
        var cards = visibleBooks.Select(n =>
        {
            var firstPage = pages.Where(p => p.NotebookId == n.Id).OrderBy(p => p.Position).FirstOrDefault();
            var count = pages.Count(p => p.NotebookId == n.Id);
            var colors = new[] { "#FF7668B8", "#FF548C85", "#FF6585AE", "#FFAD8D5C", "#FF98768F" };
            var accent = colors[(uint)n.Id.GetHashCode() % colors.Length];
            var thumbnail = firstPage is not null && thumbnails.TryGetValue(firstPage.Id, out var image) ? image :
                CreateThumbnail(new PageDocument(PageDocument.CurrentVersion, (n.Paper ?? PaperSettings.Default).Width, (n.Paper ?? PaperSettings.Default).Height, [], n.Paper));
            var opened = n.LastOpened == 0 ? "Not opened yet" :
                DateTimeOffset.FromUnixTimeMilliseconds(n.LastOpened).LocalDateTime.Date == DateTime.Now.Date ? "Opened today" : "Opened recently";
            return new HomeNotebookItem(n, $"{count} page{(count == 1 ? "" : "s")} · {opened}", FolderPath(n.FolderId), accent, "#12" + accent[3..], thumbnail);
        }).ToList();
        var visibleFolders = folders.Where(f => query.Length > 0 ? FolderPath(f.Id).Contains(query, StringComparison.OrdinalIgnoreCase) :
            homeSection == "library" && f.ParentId == homeFolder).OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase).Select(f =>
                new HomeFolderItem(f.Id, f.Name, FolderDetail(f.Id))).ToList();
        var crumbs = new List<HomeBreadcrumb> { new(null, "Library") };
        var chain = new List<FolderSummary>(); var cursor = homeFolder; var visited = new HashSet<Guid>();
        while (cursor is Guid value && visited.Add(value) && folders.FirstOrDefault(f => f.Id == value) is { } folder)
        { chain.Insert(0, folder); cursor = folder.ParentId; }
        crumbs.AddRange(chain.Select(f => new HomeBreadcrumb(f.Id, f.Name)));
        var title = query.Length > 0 ? "Search your library" : homeFolder is Guid folderId ? folders.First(f => f.Id == folderId).Name :
            homeSection switch { "recent" => "Recently opened", "starred" => "Keep close", _ => "Your notebooks" };
        var subtitle = query.Length > 0 ? $"Results for “{query}”" : homeSection == "starred" ? "The notebooks you reach for most." :
            homeFolder is not null ? FolderPath(homeFolder) : "A little structure. More room to think.";
        var activePage = pages.FirstOrDefault(p => p.Id == currentPageId);
        var activeBook = notebooks.FirstOrDefault(n => n.Id == activePage?.NotebookId);
        HomeView.Render(title, subtitle, homeSection, crumbs, FolderNavigationItems(), visibleFolders, cards, activeBook?.Name, activePage?.Name);
        if (!refreshingCovers) _ = LoadHomeCoversAsync(visibleBooks);
    }
    private string FolderDetail(Guid id)
    {
        var books = notebooks.Count(n => n.FolderId == id); var children = folders.Count(f => f.ParentId == id);
        return $"{books} notebook{(books == 1 ? "" : "s")} · {children} folder{(children == 1 ? "" : "s")}";
    }
    private async Task LoadHomeCoversAsync(IReadOnlyList<NotebookSummary> books)
    {
        var generation = ++homeCoverGeneration;
        foreach (var book in books.Take(160))
        {
            var first = pages.Where(p => p.NotebookId == book.Id).OrderBy(p => p.Position).FirstOrDefault();
            if (first is null || thumbnails.ContainsKey(first.Id)) continue;
            if (writer is null || !homeVisible || generation != homeCoverGeneration) return;
            try
            {
                var document = await writer.ReadAsync(db => db.LoadPage(first.Id));
                if (writer is null || !homeVisible || generation != homeCoverGeneration) return;
                UpdateThumbnail(first.Id, document);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or TaskCanceledException) { return; }
        }
        if (generation == homeCoverGeneration && homeVisible) RefreshHomeCardsOnly();
    }
    private void RefreshHomeCardsOnly()
    {
        // Rebind once after the cover batch. Cached covers make the next refresh synchronous.
        if (homeVisible) { homeCoverGeneration++; RefreshHomeWithoutCoverLoop(); }
    }
    private bool refreshingCovers;
    private void RefreshHomeWithoutCoverLoop()
    {
        if (refreshingCovers) return;
        refreshingCovers = true;
        try { RefreshHome(); } finally { refreshingCovers = false; }
    }
    public async Task<Guid> CreateFolderAsync(string name, Guid? parent = null)
    {
        if (writer is null) throw new InvalidOperationException("Library is not open.");
        ValidateName(name);
        if (parent is not null && folders.All(f => f.Id != parent)) throw new InvalidOperationException("The parent folder does not exist.");
        var folder = new FolderSummary(Guid.NewGuid(), name.Trim(), parent);
        await writer.MutateAsync(db => { db.CreateFolder(folder); return true; });
        folders.Add(folder); RefreshHome(); return folder.Id;
    }
    public async Task MoveNotebookAsync(Guid id, Guid? destination)
    {
        if (writer is null) return;
        if (destination is not null && folders.All(f => f.Id != destination)) throw new InvalidOperationException("The destination folder does not exist.");
        await writer.MutateAsync(db => { db.MoveNotebook(id, destination); return true; });
        ReplaceNotebook(notebooks.First(n => n.Id == id) with { FolderId = destination }); RefreshHome();
    }
    public async Task MoveFolderAsync(Guid id, Guid? destination)
    {
        if (writer is null) return;
        var blocked = FolderDescendants(id);
        if (destination is Guid parent && (blocked.Contains(parent) || folders.All(f => f.Id != parent)))
            throw new InvalidOperationException("Choose a folder outside this folder and its subfolders.");
        var folder = folders.First(f => f.Id == id);
        await writer.MutateAsync(db => { db.MoveFolder(id, destination); return true; });
        folders[folders.IndexOf(folder)] = folder with { ParentId = destination }; RefreshHome();
    }
    private HashSet<Guid> FolderDescendants(Guid id)
    {
        var ids = new HashSet<Guid> { id };
        while (true)
        {
            var count = ids.Count;
            foreach (var folder in folders) if (folder.ParentId is Guid parent && ids.Contains(parent)) ids.Add(folder.Id);
            if (count == ids.Count) return ids;
        }
    }
    private void ReplaceNotebook(NotebookSummary notebook)
    {
        var wasBinding = bindingSelection; bindingSelection = true;
        try
        {
            var previous = notebooks.First(n => n.Id == notebook.Id);
            notebooks[notebooks.IndexOf(previous)] = notebook; UpdateNotebookTab(notebook);
        }
        finally { bindingSelection = wasBinding; }
    }
    public void RenameNotebook(Guid id, string name)
    {
        if (writer is null) return; ValidateName(name);
        var notebook = notebooks.First(n => n.Id == id);
        ReplaceNotebook(notebook with { Name = name.Trim() });
        writer.Write(db => db.RenameNotebook(id, name.Trim())); BindNavigation(); RefreshHome();
    }
    public void SetNotebookFavorite(Guid id, bool favorite)
    {
        if (writer is null) return;
        ReplaceNotebook(notebooks.First(n => n.Id == id) with { Favorite = favorite });
        writer.Write(db => db.FavoriteNotebook(id, favorite)); RefreshHome();
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 80) throw new ArgumentException("Use a name between 1 and 80 characters.");
    }
    private async void HomeAction(string action, Guid id)
    {
        try
        {
            if (action == "copy-notebook") { await CopyNotebookAsync(id); return; }
            if (action == "duplicate-notebook") { await DuplicateNotebookAsync(id); return; }
            if (action == "delete-notebook") { await ConfirmDeleteNotebookAsync(id); return; }
            var isFolder = action.EndsWith("folder", StringComparison.Ordinal);
            var folder = isFolder ? folders.First(f => f.Id == id) : null;
            var notebook = !isFolder ? notebooks.First(n => n.Id == id) : null;
            if (action.StartsWith("rename", StringComparison.Ordinal))
            {
                var name = NameDialog.Ask(this, isFolder ? "Rename folder" : "Rename notebook", folder?.Name ?? notebook!.Name);
                if (name is null) return;
                if (folder is not null && writer is not null)
                { writer.Write(db => db.RenameFolder(id, name)); folders[folders.IndexOf(folder)] = folder with { Name = name }; RefreshHome(); }
                else RenameNotebook(id, name);
            }
            else if (action == "favorite-notebook") SetNotebookFavorite(id, !notebook!.Favorite);
            else
            {
                var excluded = isFolder ? FolderDescendants(id) : [];
                var choices = new List<FolderDestination> { new(null, "Library") };
                choices.AddRange(folders.Where(f => !excluded.Contains(f.Id)).OrderBy(f => FolderPath(f.Id)).Select(f => new FolderDestination(f.Id, FolderPath(f.Id))));
                var destination = DestinationDialog.Ask(this, isFolder ? "Move folder" : "Move notebook", choices, folder?.ParentId ?? notebook?.FolderId);
                if (destination is null) return;
                if (isFolder) await MoveFolderAsync(id, destination.Id); else await MoveNotebookAsync(id, destination.Id);
            }
        }
        catch (Exception ex) { ShowError(ex); }
    }
}
