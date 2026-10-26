using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly ObservableCollection<NotebookSummary> openNotebooks = [];
    private readonly Dictionary<Guid, Guid> notebookPages = [];
    private readonly HashSet<Guid> favorites = [];
    private readonly Dictionary<Guid, NavigationPage> navigationPages = [];
    private readonly Dictionary<Guid, ImageSource> thumbnails = [];
    private readonly DispatcherTimer thumbnailTimer = new() { Interval = TimeSpan.FromMilliseconds(800) };
    private int thumbnailGeneration;
    private bool thumbnailTimerInitialized;
    private WorkspacePreferences preferences = new();
    private bool favoritesOnly;
    private bool focusMode;
    private bool sidebarBeforeFocus;
    private bool compact;
    private WindowState stateBeforeFocus;

    private void RestoreNotebookTabs()
    {
        foreach (var (book, page) in preferences.NotebookPages ?? [])
            if (pages.Any(p => p.Id == page && p.NotebookId == book)) notebookPages[book] = page;
        // Collapse page tabs from preview.1 into one tab per notebook.
        var legacyBooks = new List<Guid>();
        foreach (var id in preferences.OpenPages ?? [])
            if (pages.FirstOrDefault(p => p.Id == id) is { } page)
            { legacyBooks.Add(page.NotebookId); notebookPages.TryAdd(page.NotebookId, page.Id); }
        foreach (var id in (preferences.OpenNotebooks ?? legacyBooks).Distinct())
            if (notebooks.FirstOrDefault(n => n.Id == id) is { } book) openNotebooks.Add(book);
    }
    private void UpdateNotebookTab(NotebookSummary notebook)
    {
        var tab = openNotebooks.FirstOrDefault(n => n.Id == notebook.Id);
        if (tab is not null) openNotebooks[openNotebooks.IndexOf(tab)] = notebook;
    }
    private void ResetPageFilters()
    {
        var wasBinding = bindingSelection; bindingSelection = true;
        try
        {
            SearchBox.Clear(); favoritesOnly = false; FavoriteFilterButton.Background = Brushes.Transparent;
        }
        finally { bindingSelection = wasBinding; }
    }
    private string? pageListScope;
    private int pageListGeneration;
    private void BindPageList()
    {
        if (NotebookPicker.SelectedItem is not NotebookSummary book) return;
        var query = SearchBox.Text.Trim();
        var ordered = pages.Where(p => p.NotebookId == book.Id && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            (!favoritesOnly || favorites.Contains(p.Id))).OrderBy(p => p.Position).ToList();
        string Detail(PageSummary p) => $"{(favorites.Contains(p.Id) ? "★  " : "")}Page {p.Position + 1}";
        var existing = PageList.ItemsSource as List<NavigationPage>;
        var unchanged = existing is not null && existing.Count == ordered.Count &&
            existing.Zip(ordered).All(pair => pair.First.Page == pair.Second && pair.First.Detail == Detail(pair.Second));
        var scope = $"{book.Id}/{query}/{favoritesOnly}";
        var offset = pageListScope == scope ? FindTabScrollViewer(PageList)?.VerticalOffset : null;
        pageListScope = scope;
        var result = unchanged ? existing! : ordered.Select(p =>
        {
            var item = new NavigationPage(p) { Detail = Detail(p) };
            if (thumbnails.TryGetValue(p.Id, out var thumbnail)) item.Thumbnail = thumbnail;
            else item.Thumbnail = CreateThumbnail(new PageDocument(PageDocument.CurrentVersion,
                (p.Paper ?? PaperSettings.Default).Width, (p.Paper ?? PaperSettings.Default).Height, [], p.Paper));
            navigationPages[p.Id] = item;
            return item;
        }).ToList();
        var selection = result.FirstOrDefault(p => p.Id == currentPageId);
        var changedSelection = PageList.SelectedItem != selection;
        var generation = ++pageListGeneration;
        if (!unchanged) PageList.ItemsSource = result;
        PageList.SelectedItem = selection;
        if (!unchanged || changedSelection)
            Dispatcher.BeginInvoke(() =>
            {
                if (generation != pageListGeneration) return;
                if (!unchanged && offset is double saved) FindTabScrollViewer(PageList)?.ScrollToVerticalOffset(saved);
                else if (selection is not null) PageList.ScrollIntoView(selection);
            }, DispatcherPriority.Loaded);
        PagesLabel.Text = favoritesOnly ? "FAVORITES" : $"PAGES  ·  {pages.Count(p => p.NotebookId == book.Id)}";
        EmptyPages.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (PageList is null || !ready || bindingSelection) return;
        bindingSelection = true;
        try { BindPageList(); }
        finally { bindingSelection = false; }
    }
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (PageFromAction(sender)?.Id is not Guid id) return;
        if (!favorites.Add(id)) favorites.Remove(id);
        BindNavigation(); SavePreferences();
    }
    private void FavoriteFilter_Click(object sender, RoutedEventArgs e)
    {
        favoritesOnly = !favoritesOnly;
        FavoriteFilterButton.Background = favoritesOnly ? (Brush)FindResource("Selected") : Brushes.Transparent;
        BindNavigation();
    }
    private async void Tab_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (DocumentTabs.SelectedItem is { } selected)
            _ = Dispatcher.BeginInvoke(() => DocumentTabs.ScrollIntoView(selected), DispatcherPriority.Loaded);
        if (bindingSelection || busy || DocumentTabs.SelectedItem is not NotebookSummary tab ||
            (!homeVisible && pages.FirstOrDefault(p => p.Id == currentPageId)?.NotebookId == tab.Id)) return;
        try { await ActivateNotebookAsync(tab.Id); } catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    private void Tabs_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var viewer = FindTabScrollViewer(DocumentTabs);
        if (viewer is null || viewer.ScrollableWidth <= 0) return;
        viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
    private static ScrollViewer? FindTabScrollViewer(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollViewer viewer) return viewer;
            if (FindTabScrollViewer(child) is { } nested) return nested;
        }
        return null;
    }
    public async Task ActivateNotebookAsync(Guid notebookId)
    {
        var page = notebookPages.TryGetValue(notebookId, out var remembered)
            ? pages.FirstOrDefault(p => p.Id == remembered && p.NotebookId == notebookId) : null;
        page ??= pages.Where(p => p.NotebookId == notebookId).OrderBy(p => p.Position).FirstOrDefault();
        if (page is null && writer is not null && notebooks.FirstOrDefault(n => n.Id == notebookId) is { } notebook)
        {
            page = new PageSummary(Guid.NewGuid(), notebookId, "Page 1", 0, null, notebook.Paper);
            SetBusy(true);
            try { await writer.MutateAsync(db => { db.CreatePage(page); return true; }); pages.Add(page); }
            finally { SetBusy(false); }
        }
        if (page is not null) await ActivatePageAsync(page.Id);
    }
    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Guid id || busy) return;
        e.Handled = true;
        try { await CloseNotebookTabAsync(id); } catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    public async Task CloseNotebookTabAsync(Guid notebookId)
    {
        if (busy) return;
        var tab = openNotebooks.FirstOrDefault(n => n.Id == notebookId); if (tab is null) return;
        var index = openNotebooks.IndexOf(tab);
        var stayAtHome = homeVisible;
        if (openNotebooks.Count == 1)
        {
            CloseAllNotebookTabs(); return;
        }
        if (pages.FirstOrDefault(p => p.Id == currentPageId)?.NotebookId == notebookId)
        {
            var neighbor = openNotebooks[index == openNotebooks.Count - 1 ? index - 1 : index + 1];
            // Finish opening the neighbor before closing, so a failed read leaves the tab intact.
            await ActivateNotebookAsync(neighbor.Id);
        }
        bindingSelection = true;
        try { openNotebooks.Remove(tab); }
        finally { bindingSelection = false; }
        if (stayAtHome) ShowHome();
        BindNavigation();
        SavePreferences();
    }
    private void CloseAllTabs_Click(object sender, RoutedEventArgs e) => CloseAllNotebookTabs();
    public void CloseAllNotebookTabs()
    {
        if (busy) return;
        ShowHome(); ClearActivePage();
        bindingSelection = true;
        try { openNotebooks.Clear(); } finally { bindingSelection = false; }
        RefreshHome(); SavePreferences();
    }
    private void SavePreferences()
    {
        if (!ready || writer is null) return;
        preferences = preferences with { OpenPages = null, OpenNotebooks = openNotebooks.Select(n => n.Id).ToList(),
            NotebookPages = new Dictionary<Guid, Guid>(notebookPages), ToolbarPosition = toolbarPosition,
            Favorites = favorites.ToList(),
            Writing = CaptureWritingPreferences(), HomeVisible = homeVisible,
            Sidebar = focusMode ? sidebarBeforeFocus : NavigationPanel.Visibility == Visibility.Visible };
        var snapshot = preferences;
        writer.Write(db => db.SavePreferences(snapshot));
    }
    private void LibraryMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.ContextMenu is { } menu)
        { menu.PlacementTarget = button; menu.IsOpen = true; }
    }
    private void Theme_Click(object sender, RoutedEventArgs e) => SetDarkTheme(!preferences.Dark);
    public void SetDarkTheme(bool dark)
    {
        preferences = preferences with { Dark = dark }; WorkspaceTheme.Apply(dark);
        ThemeIcon.Kind = dark ? "Sun" : "Moon"; SavePreferences();
        if (currentPageId is not null) BindNavigation();
        RefreshHome();
    }
    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (homeVisible) { HomeView.ToggleRail(); return; }
        if (focusMode) { ToggleFocus(); return; }
        NavigationPanel.Visibility = NavigationPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        AnimatePanel(NavigationPanel); SavePreferences();
    }
    private void Focus_Click(object sender, RoutedEventArgs e) => ToggleFocus();
    public void ToggleFocus()
    {
        if (homeVisible) return;
        var previousView = Viewport.View;
        var previousSize = new Size(Viewport.ActualWidth, Viewport.ActualHeight);
        var previousPage = currentPageId;
        CommitTextEditor(); CloseToolInspector(); ZoomPopup.IsOpen = false; selectionMenu.IsOpen = false;
        focusMode = !focusMode;
        if (focusMode)
        {
            sidebarBeforeFocus = NavigationPanel.Visibility == Visibility.Visible;
            stateBeforeFocus = WindowState; WindowState = WindowState.Maximized;
            NavigationPanel.Visibility = Visibility.Collapsed; ContextPanel.Visibility = Visibility.Collapsed;
            TabStrip.Visibility = Visibility.Collapsed; PageHeader.Visibility = Visibility.Collapsed;
            FocusButton.Background = (Brush)FindResource("Selected");
        }
        else
        {
            WindowState = stateBeforeFocus;
            NavigationPanel.Visibility = sidebarBeforeFocus && !compact ? Visibility.Visible : Visibility.Collapsed;
            TabStrip.Visibility = Visibility.Visible; PageHeader.Visibility = Visibility.Visible;
            FocusButton.Background = Brushes.Transparent;
        }
        Dispatcher.BeginInvoke(() =>
        {
            ApplyToolbarPosition();
            UpdateSelectionActions();
            if (!homeVisible && currentPageId == previousPage)
            {
                Viewport.SetView(previousView.Pan((Viewport.ActualWidth - previousSize.Width) / 2, (Viewport.ActualHeight - previousSize.Height) / 2));
                Viewport.CompleteGesture();
            }
        }, DispatcherPriority.Loaded);
    }
    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (NavigationPanel is null) return;
        UpdateResponsiveLayout(ActualWidth);
    }
    internal void UpdateResponsiveLayout(double width)
    {
        var nowCompact = width < 1020;
        if (nowCompact && !compact && !focusMode) NavigationPanel.Visibility = Visibility.Collapsed;
        compact = nowCompact;
        BrandLabel.Visibility = width < 1000 ? Visibility.Collapsed : Visibility.Visible;
        SaveIndicator.Visibility = width < 850 ? Visibility.Collapsed : Visibility.Visible;
        InputHint.Visibility = width < 1320 ? Visibility.Collapsed : Visibility.Visible;
        PaperButtonLabel.Visibility = width < 900 ? Visibility.Collapsed : Visibility.Visible;
        if (width < 1020 && ContextPanel.Visibility == Visibility.Visible) NavigationPanel.Visibility = Visibility.Collapsed;
        ApplyToolbarPosition();
        UpdateToolInspectorPlacement(width);
    }
    private static void AnimatePanel(FrameworkElement panel)
    {
        if (panel.Visibility != Visibility.Visible || !SystemParameters.ClientAreaAnimation) return;
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(.5, 1, TimeSpan.FromMilliseconds(160)));
    }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Paper_Click(object sender, RoutedEventArgs e)
    {
        if (ContextPanel.Visibility == Visibility.Visible && PaperSettingsEditor.Visibility == Visibility.Visible) { CloseContextPanel(); return; }
        CommitTextEditor(); CloseToolInspector(); selectionMenu.IsOpen = false;
        PaperSettingsEditor.Load(session?.Paper ?? PaperSettings.Default);
        ToolInspector.Visibility = Visibility.Collapsed; PaperSettingsEditor.Visibility = Visibility.Visible;
        ContextPanel.Width = 312; Grid.SetColumn(ContextPanel, 2);
        if (ActualWidth < 1280) NavigationPanel.Visibility = Visibility.Collapsed;
        ContextPanel.Visibility = Visibility.Visible; AnimatePanel(ContextPanel);
    }
    internal void ShowPaperPreview()
    {
        ToolInspector.Visibility = Visibility.Collapsed; PaperSettingsEditor.Visibility = Visibility.Visible;
        ContextPanel.Width = 312; Grid.SetColumn(ContextPanel, 2);
        PaperSettingsEditor.Load(session?.Paper ?? PaperSettings.Default);
        ContextPanel.Visibility = Visibility.Visible;
    }
    private void CloseContextPanel() => ContextPanel.Visibility = Visibility.Collapsed;
    public void ApplyPaper(PaperSettings paper, bool entireNotebook)
    {
        if (writer is null || session is null || currentPageId is not Guid id) return;
        if (session.Background is not null) return;
        paper.Validate(); CommitTextEditor();
        var notebookId = pages.First(p => p.Id == id).NotebookId;
        if (entireNotebook)
        {
            writer.Write(db => db.SaveNotebookPaper(notebookId, paper));
            var book = notebooks.First(n => n.Id == notebookId);
            bindingSelection = true;
            notebooks[notebooks.IndexOf(book)] = book with { Paper = paper };
            UpdateNotebookTab(book with { Paper = paper });
            bindingSelection = false;
        }
        else writer.Write(db => db.SavePaper(id, paper));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (entireNotebook ? page.NotebookId != notebookId : page.Id != id) continue;
            if (sessions.TryGetValue(page.Id, out var pdfSession) && pdfSession.Background is not null) continue;
            if (page.IsPdf) continue;
            pages[i] = page with { Paper = paper };
            if (sessions.TryGetValue(page.Id, out var cached)) cached.Paper = paper;
            thumbnails.Remove(page.Id);
        }
        session.Paper = paper;
        ConfigurePaper(); Viewport.FitPage(); BindNavigation(); ScheduleThumbnail();
        _ = LoadThumbnailsAsync();
        model.Status = entireNotebook ? "Paper updated for this notebook" : "Paper updated";
    }
    private async void PreviousPage_Click(object sender, RoutedEventArgs e) => await NavigatePageAsync(-1);
    private async void NextPage_Click(object sender, RoutedEventArgs e) => await NavigatePageAsync(1);
    private async Task NavigatePageAsync(int direction)
    {
        if (busy || currentPageId is not Guid id) return;
        var current = pages.First(p => p.Id == id);
        var siblings = pages.Where(p => p.NotebookId == current.NotebookId).OrderBy(p => p.Position).ToList();
        var index = siblings.FindIndex(p => p.Id == id) + direction;
        if (index < 0 || index >= siblings.Count) return;
        var zoom = Viewport.View.Zoom;
        try
        {
            await ActivatePageAsync(siblings[index].Id);
            var y = direction > 0 ? 88 : Math.Min(88, Viewport.ActualHeight - (session?.Paper.Height ?? 1056) * zoom - 40);
            Viewport.SetView(new PageView(zoom, (Viewport.ActualWidth - (session?.Paper.Width ?? 816) * zoom) / 2, y));
            Viewport.CompleteGesture();
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ScheduleThumbnail()
    {
        if (!thumbnailTimerInitialized)
        {
            thumbnailTimerInitialized = true;
            thumbnailTimer.Tick += (_, _) =>
            {
                thumbnailTimer.Stop();
                // Thumbnail generation never competes with the pen while it is in range.
                if (Viewport.PenInRange) { thumbnailTimer.Start(); return; }
                if (currentPageId is Guid id && session is not null) UpdateThumbnail(id, CaptureCurrent());
            };
        }
        thumbnailTimer.Stop(); thumbnailTimer.Start();
    }
    private void UpdateThumbnail(Guid id, PageDocument document)
    {
        var thumbnail = CreateThumbnail(document);
        thumbnails[id] = thumbnail;
        if (navigationPages.TryGetValue(id, out var item)) item.Thumbnail = thumbnail;
        // Bound thumbnail memory even for a very large library.
        if (thumbnails.Count > 160)
            foreach (var key in thumbnails.Keys.Where(key => key != currentPageId).Take(thumbnails.Count - 160).ToList()) thumbnails.Remove(key);
    }
    private async Task LoadThumbnailsAsync()
    {
        var generation = ++thumbnailGeneration;
        if (writer is null || NotebookPicker.SelectedItem is not NotebookSummary book) return;
        var ids = pages.Where(p => p.NotebookId == book.Id && !thumbnails.ContainsKey(p.Id)).Take(100).Select(p => p.Id).ToArray();
        foreach (var id in ids)
        {
            try
            {
                if (generation != thumbnailGeneration || writer is null) return;
                var document = await writer.ReadAsync(db => db.LoadPage(id));
                if (generation != thumbnailGeneration || writer is null) return;
                UpdateThumbnail(id, id == currentPageId ? CaptureCurrent() : document);
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            catch (Exception ex) when (ex is System.IO.IOException or InvalidOperationException or System.Threading.Tasks.TaskCanceledException)
            { return; }
        }
    }
    private static ImageSource CreateThumbnail(PageDocument document)
    {
        var paper = document.Paper ?? PaperSettings.Default;
        var drawing = new DrawingGroup();
        using (var dc = drawing.Open())
        {
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, paper.Width, paper.Height)));
            if (document.Background is { } background) dc.DrawImage(PdfService.LoadPreview(background, 200), new Rect(0, 0, paper.Width, paper.Height));
            else PaperSurface.Draw(dc, paper, new Size(paper.Width, paper.Height));
            InkMapper.Restore(document).Draw(dc);
            foreach (var text in document.Texts ?? [])
            {
                var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), text.FontSize, PaperSurface.Brush(text.Color), 1) { MaxTextWidth = text.Width };
                dc.DrawText(formatted, new Point(text.X + 2, text.Y + 2));
            }
            dc.Pop();
        }
        drawing.Freeze();
        var image = new DrawingImage(drawing); image.Freeze(); return image;
    }
}
