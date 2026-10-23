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
    private sealed class PageSession(StrokeCollection strokes)
    {
        public StrokeCollection Strokes { get; } = strokes;
        public InkHistory History { get; } = new();
        public long Visited { get; set; }
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
        InkSurface.Strokes.StrokesChanged += Strokes_Changed;
        PreviewKeyDown += Window_KeyDown;
        Viewport.ViewChanged += () => ZoomLabel.Text = $"{Viewport.View.Zoom:P0}";
        Viewport.GestureCompleted += () => { if (ready && !busy) { viewTimer.Stop(); viewTimer.Start(); } };
        viewTimer.Tick += (_, _) => { viewTimer.Stop(); SaveCurrentView(); };
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
        foreach (var page in pages) if (page.View is not null) views[page.Id] = page.View;
        RecoveryBanner.Visibility = library.RecoveredSession ? Visibility.Visible : Visibility.Collapsed;
        ready = true;
        try { await ActivatePageAsync(library.LastPageId); }
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
        model.Status = writer.Error is not null ? "Saving failed — retrying. Keep the app open; you can export a page for backup."
            : writer.Pending > 0 ? "Saving locally…" : "Saved locally";
        Title = $"{(writer.Pending > 0 || writer.Error is not null ? "* " : "")}Surface Notebook · {pages.FirstOrDefault(p => p.Id == currentPageId)?.Name}";
    }
    private void SetBusy(bool value)
    {
        busy = value;
        Toolbar.IsEnabled = !value;
        NavigationControls.IsEnabled = !value;
        InkSurface.IsEnabled = !value;
        if (!value) { RefreshHistory(); UpdateSaveStatus(); }
    }

    private void ApplyPen()
    {
        if (InkSurface is null || ColorPicker?.SelectedItem is not ComboBoxItem item) return;
        // Identical pressure/curve settings to the Surface-tested v0.1 writing path.
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString((string)item.Tag),
            Width = model.Thickness, Height = model.Thickness, IgnorePressure = false, FitToCurve = true
        };
    }
    private void Strokes_Changed(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (restoring || session is null || currentPageId is not Guid page || writer is null) return;
        InkMapper.EnsureIdentities(e.Added);
        var added = InkMapper.Capture(e.Added).Strokes.Select((s, i) => new StoredStroke(s, i)).ToArray();
        var removed = InkMapper.Capture(e.Removed).Strokes.Select(s => s.Id).ToArray();
        writer.Write(db => db.ApplyDelta(page, added, removed));
        session.History.Record(InkSurface.Strokes);
        RefreshHistory();
        UpdateSaveStatus();
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
    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (InkSurface is not null)
            InkSurface.EditingMode = PenButton.IsChecked == true ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.EraseByStroke;
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e) => ApplyPen();
    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyPen();
    private void Undo_Click(object sender, RoutedEventArgs e) => ApplyHistory(undo: true);
    private void Redo_Click(object sender, RoutedEventArgs e) => ApplyHistory(undo: false);
    private void ApplyHistory(bool undo)
    {
        if (session is null || currentPageId is not Guid id || writer is null ||
            (undo ? !session.History.CanUndo : !session.History.CanRedo)) return;
        var strokes = undo ? session.History.Undo() : session.History.Redo();
        restoring = true;
        try { InkSurface.Strokes.Clear(); InkSurface.Strokes.Add(strokes); }
        finally { restoring = false; }
        var document = InkMapper.Capture(InkSurface.Strokes);
        writer.Write(db => db.ReplacePage(id, document));
        RefreshHistory();
        UpdateSaveStatus();
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (InkSurface.Strokes.Count > 0 && MessageBox.Show(this, "Clear every stroke on this page? You can undo this.",
            "Clear page", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            InkSurface.Strokes.Clear();
    }

    public async Task ActivatePageAsync(Guid id)
    {
        if (!ready || writer is null) return;
        SetBusy(true);
        model.Status = "Opening page…";
        SaveCurrentView();
        try
        {
            if (!sessions.TryGetValue(id, out var next))
            {
                var document = await writer.ReadAsync(db => db.LoadPage(id)).WaitAsync(TimeSpan.FromSeconds(10));
                next = new PageSession(InkMapper.Restore(document));
                next.History.Reset(next.Strokes);
                sessions[id] = next;
            }
            currentPageId = id;
            session = next;
            next.Visited = ++visitCounter;
            SetStrokes(next.Strokes);
            Viewport.SetPenPresence(false);
            if (views.TryGetValue(id, out var view)) Viewport.SetView(view); else Viewport.FitPage();
            writer.Write(db => db.SelectPage(id));
            BindNavigation();
            foreach (var old in sessions.Where(pair => pair.Key != id).OrderByDescending(pair => pair.Value.Visited).Skip(4).ToArray())
                sessions.Remove(old.Key);
        }
        finally { SetBusy(false); }
    }
    private void BindNavigation()
    {
        if (currentPageId is not Guid id) return;
        var page = pages.First(p => p.Id == id);
        var notebook = notebooks.First(n => n.Id == page.NotebookId);
        bindingSelection = true;
        NotebookPicker.SelectedItem = notebook;
        PageList.ItemsSource = pages.Where(p => p.NotebookId == notebook.Id).OrderBy(p => p.Position).ToList();
        PageList.SelectedItem = page;
        PageCaption.Text = $"{notebook.Name} · {page.Name}";
        bindingSelection = false;
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
        if (bindingSelection || busy || PageList.SelectedItem is not PageSummary page || page.Id == currentPageId) return;
        try { await ActivatePageAsync(page.Id); }
        catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    private async void Notebook_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (bindingSelection || busy || NotebookPicker.SelectedItem is not NotebookSummary notebook) return;
        var page = pages.Where(p => p.NotebookId == notebook.Id).OrderByDescending(p => sessions.GetValueOrDefault(p.Id)?.Visited ?? 0)
            .ThenBy(p => p.Position).First();
        try { await ActivatePageAsync(page.Id); }
        catch (Exception ex) { BindNavigation(); ShowError(ex); }
    }
    public async Task<Guid> CreateNotebookAsync(string name)
    {
        if (writer is null) throw new InvalidOperationException("Library is not open.");
        var notebook = new NotebookSummary(Guid.NewGuid(), name.Trim());
        var page = new PageSummary(Guid.NewGuid(), notebook.Id, "Page 1", 0, null);
        writer.Write(db => db.CreateNotebook(notebook, page.Id));
        notebooks.Add(notebook); pages.Add(page);
        sessions[page.Id] = new PageSession(new StrokeCollection());
        await ActivatePageAsync(page.Id);
        return page.Id;
    }
    public async Task<Guid> CreatePageAsync(string? name = null)
    {
        if (writer is null || currentPageId is not Guid current) throw new InvalidOperationException("No notebook is open.");
        var notebookId = pages.First(p => p.Id == current).NotebookId;
        var position = pages.Where(p => p.NotebookId == notebookId).Max(p => p.Position) + 1;
        var page = new PageSummary(Guid.NewGuid(), notebookId, name ?? $"Page {position + 1}", position, null);
        writer.Write(db => db.CreatePage(page));
        pages.Add(page); sessions[page.Id] = new PageSession(new StrokeCollection());
        await ActivatePageAsync(page.Id);
        return page.Id;
    }
    private async void AddNotebook_Click(object sender, RoutedEventArgs e)
    {
        var name = NameDialog.Ask(this, "New notebook", "New notebook");
        if (name is not null) try { await CreateNotebookAsync(name); } catch (Exception ex) { ShowError(ex); }
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
        bindingSelection = true;
        notebooks[notebooks.IndexOf(notebook)] = notebook with { Name = name };
        bindingSelection = false;
        writer.Write(db => db.RenameNotebook(notebook.Id, name));
        BindNavigation();
    }
    private void RenamePage_Click(object sender, RoutedEventArgs e)
    {
        if (PageList.SelectedItem is not PageSummary page || writer is null) return;
        var name = NameDialog.Ask(this, "Rename page", page.Name);
        if (name is null) return;
        pages[pages.IndexOf(page)] = page with { Name = name };
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
            SetStrokes(strokes);
            var imported = new PageSession(strokes); imported.History.Reset(strokes);
            sessions[id] = imported; session = imported;
            writer!.Write(db => db.ReplacePage(id, document));
        }
        catch (Exception ex) { ShowError(ex); }
        finally { SetBusy(false); }
    }
    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Notebook page (*.stempage)|*.stempage", DefaultExt = ".stempage", FileName = "Page.stempage" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var document = InkMapper.Capture(InkSurface.Strokes);
            await PageStore.SaveAsync(dialog.FileName, document);
            model.Status = "Page exported · notebook autosave continues locally";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    public async Task<bool> FlushAsync()
    {
        SaveCurrentView();
        var saved = writer is null || await writer.FlushAsync(TimeSpan.FromSeconds(10));
        UpdateSaveStatus();
        return saved;
    }
    private async void Flush_Click(object sender, RoutedEventArgs e)
    {
        try { if (!await FlushAsync()) throw new IOException("Saving has not completed. Keep the app open; pending edits will be retried."); UpdateSaveStatus(); }
        catch (Exception ex) { ShowError(ex); }
    }
    public async Task<bool> ShutdownLibraryAsync()
    {
        SaveCurrentView();
        if (writer is null) return true;
        if (!writer.Initialized.IsCompletedSuccessfully) { writer.Dispose(); return true; }
        if (!await writer.FinishAsync(TimeSpan.FromSeconds(10))) return false;
        writer.Changed -= WriterChanged;
        await writer.StopAsync();
        writer = null;
        return true;
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
        if (busy || Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.Z: Undo_Click(this, e); break;
            case Key.Y: Redo_Click(this, e); break;
            case Key.S: Flush_Click(this, e); break;
            case Key.N: AddPage_Click(this, e); break;
            case Key.O: Import_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) { Viewport.Zoom(1.2, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2)); Viewport.CompleteGesture(); }
    private void ZoomOut_Click(object sender, RoutedEventArgs e) { Viewport.Zoom(1 / 1.2, new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2)); Viewport.CompleteGesture(); }
    private void Fit_Click(object sender, RoutedEventArgs e) => Viewport.FitPage();
    private void ToggleNavigation_Click(object sender, RoutedEventArgs e) => NavigationPanel.Visibility = NavigationPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void DismissRecovery_Click(object sender, RoutedEventArgs e) => RecoveryBanner.Visibility = Visibility.Collapsed;
}
