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
    private readonly ObservableCollection<PageSummary> openPages = [];
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

    private void BindPageList()
    {
        if (NotebookPicker.SelectedItem is not NotebookSummary book) return;
        var query = SearchBox.Text.Trim();
        var result = pages.Where(p => p.NotebookId == book.Id && p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) &&
            (!favoritesOnly || favorites.Contains(p.Id))).OrderBy(p => p.Position).Select(p =>
        {
            var item = new NavigationPage(p) { Detail = $"{(favorites.Contains(p.Id) ? "★  " : "")}Page {p.Position + 1}" };
            if (thumbnails.TryGetValue(p.Id, out var thumbnail)) item.Thumbnail = thumbnail;
            else item.Thumbnail = CreateThumbnail(new PageDocument(PageDocument.CurrentVersion,
                (p.Paper ?? PaperSettings.Default).Width, (p.Paper ?? PaperSettings.Default).Height, [], p.Paper));
            navigationPages[p.Id] = item;
            return item;
        }).ToList();
        PageList.ItemsSource = result;
        PageList.SelectedItem = result.FirstOrDefault(p => p.Id == currentPageId);
        PagesLabel.Text = favoritesOnly ? "FAVORITES" : $"PAGES  ·  {pages.Count(p => p.NotebookId == book.Id)}";
        EmptyPages.Visibility = result.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SearchPlaceholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (PageList is null || !ready) return;
        bindingSelection = true;
        try { BindPageList(); }
        finally { bindingSelection = false; }
    }
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (currentPageId is not Guid id) return;
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
        if (bindingSelection || busy || DocumentTabs.SelectedItem is not PageSummary tab || tab.Id == currentPageId) return;
        try { await ActivatePageAsync(tab.Id); } catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not Guid id || busy) return;
        e.Handled = true;
        var tab = openPages.FirstOrDefault(p => p.Id == id); if (tab is null) return;
        if (openPages.Count == 1)
        {
            // The current document always has one tab; closing a tab never deletes notes.
            model.Status = "This is your only open page"; return;
        }
        var index = openPages.IndexOf(tab);
        bindingSelection = true; openPages.Remove(tab); bindingSelection = false;
        if (id == currentPageId)
            try { await ActivatePageAsync(openPages[Math.Min(index, openPages.Count - 1)].Id); } catch (Exception ex) { ShowError(ex); }
        SavePreferences();
    }
    private void SavePreferences()
    {
        if (!ready || writer is null) return;
        preferences = preferences with { OpenPages = openPages.Select(p => p.Id).ToList(), Favorites = favorites.ToList(),
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
    }
    private void ToggleNavigation_Click(object sender, RoutedEventArgs e)
    {
        if (focusMode) { ToggleFocus(); return; }
        NavigationPanel.Visibility = NavigationPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        AnimatePanel(NavigationPanel); SavePreferences();
    }
    private void Focus_Click(object sender, RoutedEventArgs e) => ToggleFocus();
    public void ToggleFocus()
    {
        CommitTextEditor(); ToolPopup.IsOpen = false;
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
        Dispatcher.BeginInvoke(() => { toolbarTranslation.X = 0; toolbarTranslation.Y = 0; Viewport.FitPage(); }, DispatcherPriority.Loaded);
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
        SaveIndicator.Visibility = width < 1120 ? Visibility.Collapsed : Visibility.Visible;
        InputHint.Visibility = width < 880 ? Visibility.Collapsed : Visibility.Visible;
        PaperButtonLabel.Visibility = width < 900 ? Visibility.Collapsed : Visibility.Visible;
        if (width < 1020 && ContextPanel.Visibility == Visibility.Visible) NavigationPanel.Visibility = Visibility.Collapsed;
        toolbarTranslation.X = 0; toolbarTranslation.Y = 0;
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
        if (ContextPanel.Visibility == Visibility.Visible) { CloseContextPanel(); return; }
        CommitTextEditor(); ToolPopup.IsOpen = false;
        PaperSettingsEditor.Load(session?.Paper ?? PaperSettings.Default);
        if (ActualWidth < 1280) NavigationPanel.Visibility = Visibility.Collapsed;
        ContextPanel.Visibility = Visibility.Visible; AnimatePanel(ContextPanel);
    }
    internal void ShowPaperPreview()
    {
        PaperSettingsEditor.Load(session?.Paper ?? PaperSettings.Default);
        ContextPanel.Visibility = Visibility.Visible;
    }
    private void CloseContextPanel() => ContextPanel.Visibility = Visibility.Collapsed;
    public void ApplyPaper(PaperSettings paper, bool entireNotebook)
    {
        if (writer is null || session is null || currentPageId is not Guid id) return;
        paper.Validate(); CommitTextEditor();
        var notebookId = pages.First(p => p.Id == id).NotebookId;
        if (entireNotebook)
        {
            writer.Write(db => db.SaveNotebookPaper(notebookId, paper));
            var book = notebooks.First(n => n.Id == notebookId);
            bindingSelection = true; notebooks[notebooks.IndexOf(book)] = book with { Paper = paper }; bindingSelection = false;
        }
        else writer.Write(db => db.SavePaper(id, paper));
        for (var i = 0; i < pages.Count; i++)
        {
            var page = pages[i];
            if (entireNotebook ? page.NotebookId != notebookId : page.Id != id) continue;
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
            PaperSurface.Draw(dc, paper, new Size(paper.Width, paper.Height));
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
