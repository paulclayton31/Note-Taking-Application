using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private bool CalculationMode => toolName == "Calculate" && !barrelSelection;
    private void UpdateCalculationActions()
    {
        if (CalculationActionsPanel is null) return;
        CalculationActionsPanel.Visibility = ready && !busy && !homeVisible && CalculationMode ? Visibility.Visible : Visibility.Collapsed;
        ReviewCalculationButton.IsEnabled = HasSelection;
    }
    private void NewCalculation_Click(object sender, RoutedEventArgs e) => OpenCalculationEditor(useSelection: false);
    private void ReviewCalculation_Click(object sender, RoutedEventArgs e) => OpenCalculationEditor(useSelection: true);
    private void OpenLegacyCalculationEditor(bool useSelection, Guid? editingId = null)
    {
        if (!ready || busy || homeVisible || session is null) return;
        CommitTextEditor(); CloseToolInspector(); selectionMenu.IsOpen = false;
        var existing = editingId is Guid id ? session.Texts.FirstOrDefault(t => t.Id == id && t.CalculationSource is not null) : null;
        var strokes = useSelection && existing is null ? InkSurface.GetSelectedStrokes().Clone() : new StrokeCollection();
        var selectedIds = useSelection ? InkSurface.GetSelectedElements().OfType<TextBox>().Select(b => (Guid)b.Tag).ToHashSet() : [];
        var selected = session.Texts.Where(t => selectedIds.Contains(t.Id)).ToList();
        if (existing is null && strokes.Count == 0 && selected.Count == 1 && selected[0].CalculationSource is not null)
            existing = selected[0];
        var source = existing?.CalculationSource ?? string.Join(Environment.NewLine, selected.Select(t => t.CalculationSource ?? t.Text));
        var bounds = InkSurface.GetSelectionBounds();
        var center = Viewport.TranslatePoint(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), InkSurface);
        var position = existing is not null ? new Point(existing.X, existing.Y) : useSelection && !bounds.IsEmpty
            ? new Point(bounds.Left, bounds.Bottom + 18) : center;
        var dialog = new CalculationDialog(InkMapper.Capture(strokes).Strokes, source, existing is not null) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        try { SaveCalculation(dialog.Source, position, existing?.Id); }
        catch (Exception ex) when (ex is CalculationEngine.CalculationException or InvalidOperationException) { ShowError(ex); }
    }
    private void OpenCalculationEditor(bool useSelection, Guid? editingId = null, Rect? region = null)
    {
        if (!ready || busy || homeVisible || session is null) return;
        CommitTextEditor(); CloseToolInspector(); selectionMenu.IsOpen = false;
        var existing = editingId is Guid id ? session.Texts.FirstOrDefault(t => t.Id == id) : null;
        if (existing is null && useSelection && InkSurface.GetSelectedStrokes().Count == 0 && InkSurface.GetSelectedElements().Count == 1)
            existing = session.Texts.FirstOrDefault(t => t.Id == (Guid)((FrameworkElement)InkSurface.GetSelectedElements()[0]).Tag && t.CalculationSource is not null);
        if (existing is { AiCalculation: null, CalculationSource: not null })
        { OpenLegacyCalculationEditor(useSelection, existing.Id); return; }
        try
        {
            var bounds = region ?? InkSurface.GetSelectionBounds();
            byte[]? png = existing?.AiCalculation?.ImagePng;
            if (existing is null && (useSelection || region is not null))
            {
                if (bounds.IsEmpty) { model.Status = "Circle a problem first, or choose Select area for a PDF or image."; return; }
                bounds.Inflate(8, 8); png = CalculationCapture.Create(CaptureCurrent(), bounds);
            }
            var center = Viewport.TranslatePoint(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), InkSurface);
            var position = existing is not null ? new Point(existing.X, existing.Y) : !bounds.IsEmpty && (useSelection || region is not null)
                ? new Point(bounds.Left, bounds.Bottom + 18) : center;
            var originalPage = currentPageId;
            var dialog = new GeminiCalculationDialog(png, existing?.AiCalculation?.Instruction ?? "",
                new GeminiKeyStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SurfaceNotebook", "Credentials")), existing?.AiCalculation) { Owner = this };
            if (dialog.ShowDialog() == true && originalPage == currentPageId && dialog.Result is { } result)
                SaveAiCalculation(result, position, existing?.Id);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or NotSupportedException) { ShowError(ex); }
    }
    public Guid SaveAiCalculation(AiCalculation result, Point position, Guid? existingId = null)
    {
        if (!ready || busy || homeVisible || session is null) throw new InvalidOperationException("Open a page before adding a solution.");
        result.Validate(); CommitTextEditor();
        var existing = existingId is Guid id ? session.Texts.FirstOrDefault(t => t.Id == id) : null;
        if (existingId is not null && existing is null) throw new InvalidOperationException("The calculation no longer exists.");
        if (existing is null && session.Texts.Count >= 2000) throw new InvalidOperationException("This page has too many text objects.");
        var width = Math.Min(540, session.Paper.Width - 32);
        var card = existing is null ? new PageText(Guid.NewGuid(),
            Math.Clamp(double.IsFinite(position.X) ? position.X : 16, 16, Math.Max(16, session.Paper.Width - width - 16)),
            Math.Clamp(double.IsFinite(position.Y) ? position.Y : 16, 16, Math.Max(16, session.Paper.Height - 100)),
            width, 16, penColor, result.DisplayText, result.Problem, result)
            : existing with { Text = result.DisplayText, CalculationSource = result.Problem, AiCalculation = result };
        var texts = session.Texts.Where(t => t.Id != card.Id).Append(card).ToList();
        (CaptureCurrent() with { Texts = texts }).Validate(enforceSizeLimits: false);
        session.Texts = texts; ClearPageSelection(); RenderTexts(); SaveFullPage(); UpdateSelectionActions();
        model.Status = "Gemini solution saved · tap the card with Calculate to review";
        return card.Id;
    }
    public Guid SaveCalculation(string source, Point position, Guid? existingId = null)
    {
        if (!ready || busy || homeVisible || session is null) throw new InvalidOperationException("Open a page before adding a calculation.");
        var result = CalculationEngine.Evaluate(source);
        CommitTextEditor();
        var existing = existingId is Guid id ? session.Texts.FirstOrDefault(t => t.Id == id && t.CalculationSource is not null) : null;
        if (existingId is not null && existing is null) throw new InvalidOperationException("The calculation no longer exists.");
        if (existing is null && session.Texts.Count >= 2000) throw new InvalidOperationException("This page has too many text objects. Add a calculation on another page.");
        var width = Math.Min(380, session.Paper.Width - 32);
        var estimatedHeight = Math.Min(session.Paper.Height - 32, result.Rows.Count * 30 + 16);
        var card = existing is null ? new PageText(Guid.NewGuid(),
            Math.Clamp(double.IsFinite(position.X) ? position.X : 16, 16, Math.Max(16, session.Paper.Width - width - 16)),
            Math.Clamp(double.IsFinite(position.Y) ? position.Y : 16, 16, Math.Max(16, session.Paper.Height - estimatedHeight - 16)),
            width, 18, penColor, result.DisplayText, source)
            : existing with { Text = result.DisplayText, CalculationSource = source };
        if (existing is null) session.Texts.Add(card); else session.Texts[session.Texts.IndexOf(existing)] = card;
        InkSurface.Select(new StrokeCollection(), Array.Empty<UIElement>());
        RenderTexts(); SaveFullPage(); UpdateSelectionActions();
        model.Status = "Calculation saved · use Calculate to tap and edit it";
        return card.Id;
    }
    private void CalculationCard_Down(object sender, MouseButtonEventArgs e)
    {
        if (!CalculationMode || busy || e.ChangedButton != MouseButton.Left || sender is not TextBox box ||
            e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch) return;
        e.Handled = true;
        var page = currentPageId; var id = (Guid)box.Tag;
        Dispatcher.BeginInvoke(() => { if (page == currentPageId && CalculationMode) OpenCalculationEditor(false, id); });
    }
    private void UpdateTextInputModes()
    {
        foreach (var box in InkSurface.Children.OfType<TextBox>())
        {
            var calculation = session?.Texts.FirstOrDefault(t => t.Id == (Guid)box.Tag)?.CalculationSource is not null;
            box.IsReadOnly = barrelSelection || toolName != "Text" || calculation;
            box.IsHitTestVisible = !barrelSelection && (toolName == "Text" || CalculationMode && calculation);
        }
    }
}
