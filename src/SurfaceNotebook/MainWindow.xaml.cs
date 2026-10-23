using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SurfaceNotebook.Services;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow : Window
{
    private readonly PenViewModel model = new();
    private readonly InkHistory history = new();
    private bool restoring;
    private bool dirty;
    private bool busy;
    private long revision;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = model;
        InkSurface.Strokes.StrokesChanged += Strokes_Changed;
        PreviewKeyDown += Window_KeyDown;
        ApplyPen();
        RefreshHistory();
    }

    private void ApplyPen()
    {
        if (InkSurface is null || ColorPicker?.SelectedItem is not ComboBoxItem item) return;
        InkSurface.DefaultDrawingAttributes = new DrawingAttributes
        {
            Color = (Color)ColorConverter.ConvertFromString((string)item.Tag),
            Width = model.Thickness, Height = model.Thickness,
            IgnorePressure = false, FitToCurve = true
        };
    }

    private void Strokes_Changed(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (restoring) return;
        InkMapper.EnsureIdentities(InkSurface.Strokes);
        history.Record(InkSurface.Strokes);
        MarkEdited();
    }

    private void MarkEdited()
    {
        dirty = true;
        revision++;
        model.Status = $"Unsaved changes · {InkSurface.Strokes.Count} strokes · touch drawing disabled";
        RefreshHistory();
    }

    private void RefreshHistory()
    {
        UndoButton.IsEnabled = history.CanUndo;
        RedoButton.IsEnabled = history.CanRedo;
        Title = $"{(dirty ? "* " : "")}Surface Notebook · Pen prototype";
    }

    private void SetStrokes(StrokeCollection strokes)
    {
        restoring = true;
        InkSurface.Strokes.StrokesChanged -= Strokes_Changed;
        InkSurface.Strokes = strokes;
        InkSurface.Strokes.StrokesChanged += Strokes_Changed;
        restoring = false;
    }

    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (InkSurface is not null)
            InkSurface.EditingMode = PenButton.IsChecked == true ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.EraseByStroke;
    }
    private void Color_Changed(object sender, SelectionChangedEventArgs e) => ApplyPen();
    private void Thickness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => ApplyPen();
    private void Mouse_Changed(object sender, RoutedEventArgs e)
    {
        if (InkSurface is not null) InkSurface.AllowMouseDrawing = ((CheckBox)sender).IsChecked == true;
    }
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!history.CanUndo) return;
        SetStrokes(history.Undo());
        MarkEdited();
    }
    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!history.CanRedo) return;
        SetStrokes(history.Redo());
        MarkEdited();
    }
    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (InkSurface.Strokes.Count > 0 && MessageBox.Show(this, "Clear every stroke on this page? You can undo this.",
            "Clear page", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            InkSurface.Strokes.Clear();
    }
    private bool ConfirmDiscard() => !dirty || MessageBox.Show(this,
        "This page has unsaved changes. Discard them? Choose No to keep the page and save it first.",
        "Unsaved page", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;

    private void New_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard()) return;
        SetStrokes(new StrokeCollection());
        history.Reset(InkSurface.Strokes);
        dirty = false;
        revision++;
        model.Status = "New page · use your pen to write";
        RefreshHistory();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Filter = "Notebook page (*.stempage)|*.stempage", DefaultExt = ".stempage", FileName = "Untitled.stempage" };
        if (dialog.ShowDialog(this) != true) return;
        busy = true;
        Toolbar.IsEnabled = false;
        try
        {
            var page = InkMapper.Capture(InkSurface.Strokes);
            var savedRevision = revision;
            await PageStore.SaveAsync(dialog.FileName, page);
            if (revision == savedRevision) dirty = false;
            model.Status = dirty ? "Snapshot saved · newer edits remain unsaved" : $"Saved · {Path.GetFileName(dialog.FileName)}";
            RefreshHistory();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            ShowFileError(ex);
        }
        finally { busy = false; Toolbar.IsEnabled = true; }
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        if (busy || !ConfirmDiscard()) return;
        var dialog = new OpenFileDialog { Filter = "Notebook page (*.stempage)|*.stempage" };
        if (dialog.ShowDialog(this) != true) return;
        busy = true;
        Toolbar.IsEnabled = false;
        InkSurface.IsEnabled = false;
        try
        {
            var page = await PageStore.LoadAsync(dialog.FileName);
            var strokes = InkMapper.Restore(page);
            SetStrokes(strokes);
            history.Reset(strokes);
            dirty = false;
            revision++;
            model.Status = $"Opened · {Path.GetFileName(dialog.FileName)} · {strokes.Count} strokes";
            RefreshHistory();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            ShowFileError(ex);
        }
        finally { busy = false; Toolbar.IsEnabled = true; InkSurface.IsEnabled = true; }
    }

    private void ShowFileError(Exception ex)
    {
        model.Status = "File operation failed · your current page is still open";
        MessageBox.Show(this, ex.Message, "Could not access page", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (busy) { e.Cancel = true; return; }
        e.Cancel = !ConfirmDiscard();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (busy || Keyboard.Modifiers != ModifierKeys.Control) return;
        switch (e.Key)
        {
            case Key.Z: Undo_Click(this, e); break;
            case Key.Y: Redo_Click(this, e); break;
            case Key.S: Save_Click(this, e); break;
            case Key.O: Open_Click(this, e); break;
            case Key.N: New_Click(this, e); break;
            default: return;
        }
        e.Handled = true;
    }
}
