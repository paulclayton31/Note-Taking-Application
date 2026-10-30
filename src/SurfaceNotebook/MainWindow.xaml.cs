using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow : Window
{
    private sealed class PageSession
    {
        public StrokeCollection Strokes { get; }
        public InkHistory History { get; } = new();
        public PaperSettings Paper { get; set; }
        public List<PageText> Texts { get; set; }
        public PdfBackground? Background { get; }
        public HashSet<Guid> StrokeIds { get; } = [];
        public long Visited { get; set; }
        public PageSession(StrokeCollection strokes, PaperSettings? paper = null, List<PageText>? texts = null, PdfBackground? background = null)
        {
            Strokes = strokes; Paper = paper ?? PaperSettings.Default; Texts = texts ?? []; Background = background;
            StrokeIds.UnionWith(strokes.Select(InkMapper.Identity));
            History.Reset(strokes, Texts);
        }
    }
    private readonly PenViewModel model = new();
    private readonly ObservableCollection<NotebookSummary> notebooks = [];
    private readonly List<PageSummary> pages = [];
    private readonly Dictionary<Guid, PageSession> sessions = [];
    private readonly Dictionary<Guid, PageView> views = [];
    private readonly DispatcherTimer viewTimer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly string libraryDirectory;
    private AutosaveWriter? writer;
    private Task? initializeTask;
    private PageSession? session;
    private Guid? currentPageId;
    private long visitCounter;
    private bool ready;
    private bool busy;
    private bool bindingSelection;
    private bool restoring;
    private bool allowClose;
    private bool closing;

    public MainWindow() : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SurfaceNotebook")) { }
    public MainWindow(string libraryDirectory)
    {
        this.libraryDirectory = libraryDirectory;
        InitializeComponent();
        DataContext = model;
        NotebookPicker.ItemsSource = notebooks;
        DocumentTabs.ItemsSource = openNotebooks;
        InkSurface.Strokes.StrokesChanged += Strokes_Changed;
        PreviewKeyDown += Window_KeyDown;
        AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler((_, _) => pageDragSource = null), true);
        Viewport.ViewChanged += UpdateZoomControls;
        Viewport.GestureCompleted += () => { if (ready && !busy) { viewTimer.Stop(); viewTimer.Start(); } };
        Viewport.PageTurnRequested += async direction => await NavigatePageAsync(direction);
        viewTimer.Tick += (_, _) => { viewTimer.Stop(); SaveCurrentView(); };
        PaperSettingsEditor.ApplyRequested += ApplyPaper;
        PaperSettingsEditor.CloseRequested += CloseContextPanel;
        InitializeTools();
        InitializeHome();
        ApplyPen();
        SetBusy(true);
        model.Status = "Opening your notebooks…";
    }

    public Guid? CurrentPageId => currentPageId;
    public bool IsReady => ready;
    public Task InitializeLibraryAsync() => initializeTask ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        writer = new AutosaveWriter(libraryDirectory);
        writer.Changed += WriterChanged;
        var library = await writer.Initialized;
        foreach (var notebook in library.Notebooks) notebooks.Add(notebook);
        pages.AddRange(library.Pages);
        folders.AddRange(library.Folders ?? []);
        foreach (var page in pages) if (page.View is not null) views[page.Id] = page.View;
        preferences = library.Preferences ?? new();
        RestoreNotebookTabs();
        favorites.UnionWith((preferences.Favorites ?? []).Where(id => pages.Any(p => p.Id == id)));
        WorkspaceTheme.Apply(preferences.Dark);
        ThemeIcon.Kind = preferences.Dark ? "Sun" : "Moon";
        NavigationPanel.Visibility = preferences.Sidebar ? Visibility.Visible : Visibility.Collapsed;
        RestoreToolbarPosition();
        RestoreWritingPreferences();
        var startAtHome = preferences.HomeVisible;
        RecoveryBanner.Visibility = library.RecoveredSession ? Visibility.Visible : Visibility.Collapsed;
        ready = true;
        try
        {
            if (library.LastPageId == Guid.Empty || preferences.OpenNotebooks is { Count: 0 })
            { SetBusy(false); ShowHome(); }
            else
            {
                var initial = openNotebooks.Count > 0 && !openNotebooks.Any(n => pages.Any(p => p.Id == library.LastPageId && p.NotebookId == n.Id))
                    ? pages.First(p => p.NotebookId == openNotebooks[0].Id).Id : library.LastPageId;
                await ActivatePageAsync(initial); if (startAtHome) ShowHome();
            }
        }
        catch { ready = false; SetBusy(true); throw; }
    }
    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try { await InitializeLibraryAsync(); }
        catch (Exception ex)
        {
            model.Status = "Could not open your notebooks. Saved files have been left in place.";
            MessageBox.Show(this, $"{ex.Message}\n\nLibrary: {libraryDirectory}\nIf another copy of the app is open, close it first.",
                "Could not open notebooks", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private void WriterChanged()
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (ready && !busy) UpdateSaveStatus(); });
    }
    private void UpdateSaveStatus()
    {
        if (writer is null) return;
        model.Status = writer.Error is not null ? "Saving failed — retrying. Keep the app open; export a page for backup."
            : writer.Pending > 0 ? "Saving locally…" : "All changes saved";
        Title = $"{(writer.Pending > 0 || writer.Error is not null ? "* " : "")}Surface Notebook · {pages.FirstOrDefault(p => p.Id == currentPageId)?.Name}";
        SaveIndicator.ToolTip = model.Status;
        SaveDot.Fill = writer.Error is not null ? (Brush)FindResource("Danger") : writer.Pending > 0 ? (Brush)FindResource("Accent") : PaperSurface.Brush("#FF77A18D");
        SaveErrorBanner.Visibility = writer.Error is not null ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SetBusy(bool value)
    {
        busy = value;
        Toolbar.IsEnabled = !value;
        SavedColorBar.IsEnabled = !value;
        NavigationControls.IsEnabled = !value;
        DocumentTabs.IsEnabled = !value;
        PageHeader.IsEnabled = !value;
        ContextPanel.IsEnabled = !value;
        HomeView.IsEnabled = !value;
        InkSurface.IsEnabled = !value;
        ToolbarFrame.IsEnabled = !value;
        if (value)
        {
            CancelHeldShape(); SetBarrelSelection(false);
            CloseToolInspector(); selectionMenu.IsOpen = false;
            EditNotice.Visibility = Visibility.Collapsed; editNoticeTimer.Stop(); selectionPointerDown = null;
        }
        UpdateSelectionActions();
        if (!value) { RefreshHistory(); UpdateSaveStatus(); }
    }

    private void Strokes_Changed(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (restoring || session is null || currentPageId is not Guid page || writer is null) return;
        if (toolName == "Shape" && e.Added.Count > 0) return; // Commit the normalized shape as one undoable edit.
        // Precision erasing clones property data. Give split fragments their own IDs.
        var removedIds = e.Removed.Select(InkMapper.Identity).ToHashSet();
        session.StrokeIds.ExceptWith(removedIds);
        foreach (var stroke in e.Added)
        {
            if (removedIds.Contains(InkMapper.Identity(stroke)) || !session.StrokeIds.Add(InkMapper.Identity(stroke)))
                InkMapper.RenewIdentity(stroke);
            session.StrokeIds.Add(InkMapper.Identity(stroke));
        }
        var added = InkMapper.Capture(e.Added).Strokes.Select((s, i) => new StoredStroke(s, i)).ToArray();
        var removed = removedIds.ToArray();
        writer.Write(db => db.ApplyDelta(page, added, removed));
        session.History.Record(InkSurface.Strokes, session.Texts);
        RefreshHistory(); UpdateSaveStatus(); ScheduleThumbnail();
    }
    private PageDocument CaptureCurrent()
    {
        var paper = session?.Paper ?? PaperSettings.Default;
        return InkMapper.Capture(InkSurface.Strokes) with { Width = paper.Width, Height = paper.Height, Paper = paper, Texts = session?.Texts.ToList() ?? [], Background = session?.Background };
    }
    private void SaveFullPage(bool history = true)
    {
        if (session is null || writer is null || currentPageId is not Guid id) return;
        var document = CaptureCurrent();
        var index = pages.FindIndex(p => p.Id == id);
        if (index >= 0 && (pages[index].IsPdf != (document.Background is not null) || pages[index].Paper != document.Paper))
            pages[index] = pages[index] with { IsPdf = document.Background is not null, Paper = document.Paper };
        session.StrokeIds.Clear(); session.StrokeIds.UnionWith(InkSurface.Strokes.Select(InkMapper.Identity));
        writer.Write(db => db.ReplacePage(id, document));
        if (history) session.History.Record(InkSurface.Strokes, session.Texts);
        RefreshHistory(); UpdateSaveStatus(); ScheduleThumbnail();
    }
    private void RefreshHistory()
    {
        UndoButton.IsEnabled = !busy && session?.History.CanUndo == true;
        RedoButton.IsEnabled = !busy && session?.History.CanRedo == true;
    }
    private void SetStrokes(StrokeCollection strokes)
    {
        InkSurface.Strokes.StrokesChanged -= Strokes_Changed;
        InkSurface.Strokes = strokes;
        InkSurface.Strokes.StrokesChanged += Strokes_Changed;
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => ApplyHistory(undo: true);
    private void Redo_Click(object sender, RoutedEventArgs e) => ApplyHistory(undo: false);
    private void ApplyHistory(bool undo)
    {
        EditNotice.Visibility = Visibility.Collapsed; editNoticeTimer.Stop(); selectionMenu.IsOpen = false;
        CommitTextEditor();
        if (session is null || currentPageId is null ||
            (undo ? !session.History.CanUndo : !session.History.CanRedo)) return;
        var strokes = undo ? session.History.Undo() : session.History.Redo();
        restoring = true;
        try { InkSurface.Strokes.Clear(); InkSurface.Strokes.Add(strokes); session.Texts = session.History.Texts; }
        finally { restoring = false; }
        RenderTexts();
        SaveFullPage(history: false);
        UpdateSelectionActions();
    }

    public async Task ActivatePageAsync(Guid id)
    {
        if (!ready || writer is null || pages.All(p => p.Id != id)) return;
        CommitTextEditor();
        if (currentPageId is Guid old && session is not null) UpdateThumbnail(old, CaptureCurrent());
        SetBusy(true);
        model.Status = "Opening page…";
        SaveCurrentView();
        try
        {
            if (!sessions.TryGetValue(id, out var next))
            {
                var document = await writer.ReadAsync(db => db.LoadPage(id)).WaitAsync(TimeSpan.FromSeconds(10));
                next = new PageSession(InkMapper.Restore(document), document.Paper, document.Texts, document.Background);
                sessions[id] = next;
            }
            var page = pages.First(p => p.Id == id);
            if (pages.FirstOrDefault(p => p.Id == currentPageId)?.NotebookId != page.NotebookId) ResetPageFilters();
            currentPageId = id; session = next; next.Visited = ++visitCounter;
            SetStrokes(next.Strokes); RenderTexts(); ConfigurePaper();
            Viewport.SetPenPresence(false);
            if (views.TryGetValue(id, out var view)) Viewport.SetView(view); else Viewport.FitPage();
            var opened = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            writer.Write(db => db.SelectPage(id, opened));
            ReplaceNotebook(notebooks.First(n => n.Id == page.NotebookId) with { LastOpened = opened });
            notebookPages[page.NotebookId] = id;
            if (openNotebooks.All(n => n.Id != page.NotebookId)) openNotebooks.Add(notebooks.First(n => n.Id == page.NotebookId));
            ShowWorkspace(); BindNavigation(); SavePreferences();
            if (ContextPanel.Visibility == Visibility.Visible) PaperSettingsEditor.Load(next.Paper);
            foreach (var stale in sessions.Where(pair => pair.Key != id).OrderByDescending(pair => pair.Value.Visited).Skip(4).ToArray())
                sessions.Remove(stale.Key);
        }
        finally { SetBusy(false); }
        _ = LoadThumbnailsAsync();
    }
    private void BindNavigation()
    {
        if (currentPageId is not Guid id) return;
        var page = pages.First(p => p.Id == id);
        var notebook = notebooks.First(n => n.Id == page.NotebookId);
        bindingSelection = true;
        try
        {
            NotebookPicker.SelectedItem = notebook;
            BindPageList();
            DocumentTabs.SelectedItem = homeVisible ? null : openNotebooks.FirstOrDefault(n => n.Id == page.NotebookId);
            PageCaption.Text = page.Name;
            PaperCaption.Text = $"{notebook.Name}  /  {(session?.Paper ?? PaperSettings.Default).Label}";
            var siblings = pages.Where(p => p.NotebookId == page.NotebookId).OrderBy(p => p.Position).ToList();
            PageNumber.Text = $"{siblings.FindIndex(p => p.Id == id) + 1} / {siblings.Count}";
            PreviousPageButton.IsEnabled = siblings[0].Id != id;
            NextPageButton.IsEnabled = siblings[^1].Id != id;
            FavoriteButton.Background = favorites.Contains(id) ? (Brush)FindResource("Selected") : Brushes.Transparent;
        }
        finally { bindingSelection = false; }
    }
    private void ConfigurePaper()
    {
        var paper = session?.Paper ?? PaperSettings.Default;
        PageHost.Width = paper.Width; PageHost.Height = paper.Height;
        PaperBackground.Paper = paper;
        PaperBackground.Visibility = session?.Background is null ? Visibility.Visible : Visibility.Collapsed;
        PdfPageImage.Source = session?.Background is { } background ? PdfService.LoadPreview(background) : null;
        PaperButton.IsEnabled = session?.Background is null;
        Viewport.ConfigurePaper(paper.Width, paper.Height);
    }
    private void SaveCurrentView()
    {
        viewTimer.Stop();
        if (currentPageId is Guid page && writer is not null)
        {
            var view = Viewport.View;
            if (views.TryGetValue(page, out var old) && old == view) return;
            views[page] = view;
            writer.Write(db => db.SaveView(page, view));
        }
    }
    private async void Page_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (bindingSelection || busy || PageList.SelectedItem is not NavigationPage page || page.Id == currentPageId) return;
        try { await ActivatePageAsync(page.Id); }
        catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    private async void Notebook_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (bindingSelection || busy || NotebookPicker.SelectedItem is not NotebookSummary notebook) return;
        try { await ActivateNotebookAsync(notebook.Id); }
        catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    public async Task<Guid> CreateNotebookAsync(string name, Guid? folderId = null)
    {
        if (writer is null) throw new InvalidOperationException("Library is not open.");
        ValidateName(name);
        if (folderId is not null && folders.All(f => f.Id != folderId)) throw new ArgumentException("Choose an existing folder.");
        var notebook = new NotebookSummary(Guid.NewGuid(), name.Trim(), PaperSettings.Default, folderId);
        var page = new PageSummary(Guid.NewGuid(), notebook.Id, "Page 1", 0, null, notebook.Paper);
        writer.Write(db => db.CreateNotebook(notebook, page.Id));
        notebooks.Add(notebook); pages.Add(page);
        sessions[page.Id] = new PageSession(new StrokeCollection(), notebook.Paper);
        await ActivatePageAsync(page.Id);
        return page.Id;
    }
    public async Task<Guid> CreatePageAsync(string? name = null)
    {
        if (writer is null || currentPageId is not Guid current) throw new InvalidOperationException("No notebook is open.");
        var notebookId = pages.First(p => p.Id == current).NotebookId;
        var paper = notebooks.First(n => n.Id == notebookId).Paper ?? PaperSettings.Default;
        var position = pages.Where(p => p.NotebookId == notebookId).Max(p => p.Position) + 1;
        var page = new PageSummary(Guid.NewGuid(), notebookId, name ?? $"Page {position + 1}", position, null, paper);
        writer.Write(db => db.CreatePage(page));
        pages.Add(page); sessions[page.Id] = new PageSession(new StrokeCollection(), paper);
        await ActivatePageAsync(page.Id);
        return page.Id;
    }
    private async void AddNotebook_Click(object sender, RoutedEventArgs e)
    {
        var name = NameDialog.Ask(this, "New notebook", "New notebook");
        if (name is not null) try { await CreateNotebookAsync(name, homeVisible ? homeFolder : (NotebookPicker.SelectedItem as NotebookSummary)?.FolderId); } catch (Exception ex) { ShowError(ex); }
    }
    private async void AddPage_Click(object sender, RoutedEventArgs e)
    {
        try { await CreatePageAsync(); } catch (Exception ex) { ShowError(ex); }
    }
    private void RenameNotebook_Click(object sender, RoutedEventArgs e)
    {
        if (NotebookPicker.SelectedItem is not NotebookSummary notebook || writer is null) return;
        var name = NameDialog.Ask(this, "Rename notebook", notebook.Name);
        if (name is null) return;
        RenameNotebook(notebook.Id, name);
    }
    private void RenamePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageFromAction(sender) is not { } page || writer is null) return;
        var name = NameDialog.Ask(this, "Rename page", page.Name);
        if (name is null) return;
        var updated = page with { Name = name };
        pages[pages.IndexOf(page)] = updated;
        writer.Write(db => db.RenamePage(page.Id, name));
        BindNavigation();
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Notebook page (*.stempage)|*.stempage" };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try
        {
            var document = await PageStore.LoadAsync(dialog.FileName);
            var strokes = InkMapper.Restore(document);
            var id = await CreatePageAsync(Path.GetFileNameWithoutExtension(dialog.FileName));
            var imported = new PageSession(strokes, document.Paper ?? PaperSettings.Default with
            {
                Format = PaperFormat.Custom, CustomWidth = document.Width / 96, CustomHeight = document.Height / 96
            }, document.Texts, document.Background);
            sessions[id] = imported; session = imported; SetStrokes(strokes);
            ConfigurePaper(); RenderTexts(); Viewport.FitPage(); BindNavigation();
            SaveFullPage(history: false);
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        CommitTextEditor();
        var dialog = new SaveFileDialog { Filter = "Notebook page (*.stempage)|*.stempage", DefaultExt = ".stempage",
            FileName = pages.FirstOrDefault(p => p.Id == currentPageId)?.Name ?? "Page" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            await PageStore.SaveAsync(dialog.FileName, CaptureCurrent());
            model.Status = "Page exported";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    public async Task<bool> FlushAsync()
    {
        CommitTextEditor(); SaveCurrentView(); SavePreferences();
        var saved = writer is null || await writer.FlushAsync(TimeSpan.FromSeconds(10));
        UpdateSaveStatus(); return saved;
    }
    private async void Flush_Click(object sender, RoutedEventArgs e)
    {
        try { if (!await FlushAsync()) throw new IOException("Saving has not completed. Keep the app open; pending edits will be retried."); UpdateSaveStatus(); }
        catch (Exception ex) { ShowError(ex); }
    }
    public async Task<bool> ShutdownLibraryAsync()
    {
        CommitTextEditor(); SaveCurrentView(); SavePreferences(); thumbnailTimer.Stop(); textTimer.Stop(); writingTimer.Stop(); editNoticeTimer.Stop();
        if (writer is null) return true;
        if (!writer.Initialized.IsCompletedSuccessfully) { writer.Dispose(); return true; }
        if (!await writer.FinishAsync(TimeSpan.FromSeconds(10))) return false;
        writer.Changed -= WriterChanged;
        await writer.StopAsync(); writer = null; return true;
    }
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        e.Cancel = true;
        if (closing) return;
        closing = true; SetBusy(true);
        try
        {
            if (!await ShutdownLibraryAsync()) throw new IOException("Saving is still pending. Leave the app open and retry closing after storage is available.");
            allowClose = true; _ = Dispatcher.BeginInvoke(Close);
        }
        catch (Exception ex) { closing = false; SetBusy(false); ShowError(ex); }
    }
    private void ShowError(Exception ex)
    {
        model.Status = "The operation could not finish · your current page remains open";
        MessageBox.Show(this, ex.Message, "Surface Notebook", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFocus(); e.Handled = true; return; }
        if (e.Key == Key.Escape)
        {
            if (selectionMenu.IsOpen) selectionMenu.IsOpen = false;
            else if (ZoomPopup.IsOpen) ZoomPopup.IsOpen = false;
            else if (IsToolInspectorOpen) CloseToolInspector();
            else if (textEditor is not null) CommitTextEditor();
            else if (ContextPanel.Visibility == Visibility.Visible) CloseContextPanel();
            else if (focusMode) ToggleFocus();
            e.Handled = true; return;
        }
        if (busy) return;
        if (Keyboard.FocusedElement is not TextBox && EffectiveLasso)
        {
            Action? action = Keyboard.Modifiers == ModifierKeys.Control ? e.Key switch
            {
                Key.C => CopySelectedObjects, Key.D => DuplicateSelectedObjects, Key.V => () => PasteSelectedObjects(), _ => null
            } : Keyboard.Modifiers == ModifierKeys.None && e.Key is Key.Delete or Key.Back ? DeleteSelectedObjects : null;
            if (action is not null) { RunSelectionAction(action); e.Handled = true; return; }
        }
        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.Z: if (Keyboard.FocusedElement is TextBox) return; Undo_Click(this, e); break;
                case Key.Y: if (Keyboard.FocusedElement is TextBox) return; Redo_Click(this, e); break;
                case Key.S: Flush_Click(this, e); break;
                case Key.N: AddPage_Click(this, e); break;
                case Key.O: Import_Click(this, e); break;
                case Key.B: ToggleNavigation_Click(this, e); break;
                default: return;
            }
        }
        else if (Keyboard.Modifiers == ModifierKeys.Alt && e.Key is Key.Up or Key.Down)
        { _ = NavigatePageAsync(e.Key == Key.Up ? -1 : 1); }
        else if (Keyboard.Modifiers == ModifierKeys.None && Keyboard.FocusedElement is not TextBox and not ComboBox && !IsToolInspectorOpen)
        {
            var button = e.Key switch { Key.P => PenButton, Key.H => HighlighterButton, Key.E => EraserButton, Key.L => LassoButton,
                Key.S => ShapeButton, Key.T => TextButton, Key.V => HandButton, _ => null };
            if (button is null) return;
            if (button.IsChecked == true) ToggleToolInspector(); else button.IsChecked = true;
        }
        else return;
        e.Handled = true;
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { Viewport.Zoom(1.2, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2)); Viewport.CompleteGesture(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { Viewport.Zoom(1 / 1.2, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2)); Viewport.CompleteGesture(); }
    private void Fit_Click(object sender, RoutedEventArgs e) { Viewport.FitPage(); ZoomPopup.IsOpen = false; }
    private void FitWidth_Click(object sender, RoutedEventArgs e) { Viewport.FitWidth(); ZoomPopup.IsOpen = false; }
    private void ActualSize_Click(object sender, RoutedEventArgs e)
    {
        Viewport.SetView(new PageView(1, (Viewport.ActualWidth - Viewport.PaperWidth) / 2, 88)); Viewport.CompleteGesture();
    }
    private void DismissRecovery_Click(object sender, RoutedEventArgs e) => RecoveryBanner.Visibility = Visibility.Collapsed;
}
