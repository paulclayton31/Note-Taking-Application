using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private const string SelectionClipboardFormat = "SurfaceNotebook.Selection.v1";
    private readonly ContextMenu selectionMenu = new();
    private readonly MenuItem copySelectionItem = new() { Header = "Copy", InputGestureText = "Ctrl+C" };
    private readonly MenuItem duplicateSelectionItem = new() { Header = "Duplicate", InputGestureText = "Ctrl+D" };
    private readonly MenuItem deleteSelectionItem = new() { Header = "Delete", InputGestureText = "Del" };
    private readonly MenuItem pasteSelectionItem = new() { Header = "Paste", InputGestureText = "Ctrl+V" };
    private Point selectionMenuPoint;
    private Point? selectionPointerDown;
    private Guid? selectionPointerPage;

    private bool HasSelection => InkSurface.GetSelectedStrokes().Count > 0 || InkSurface.GetSelectedElements().Count > 0;
    private void InitializeSelectionActions()
    {
        selectionMenu.Items.Add(copySelectionItem); selectionMenu.Items.Add(duplicateSelectionItem);
        selectionMenu.Items.Add(deleteSelectionItem); selectionMenu.Items.Add(new Separator()); selectionMenu.Items.Add(pasteSelectionItem);
        copySelectionItem.Click += (_, _) => RunSelectionAction(CopySelectedObjects);
        duplicateSelectionItem.Click += (_, _) => RunSelectionAction(DuplicateSelectedObjects);
        deleteSelectionItem.Click += (_, _) => RunSelectionAction(DeleteSelectedObjects);
        pasteSelectionItem.Click += (_, _) => RunSelectionAction(() => PasteSelectedObjects(selectionMenuPoint));
        selectionMenu.Opened += (_, _) =>
        {
            copySelectionItem.IsEnabled = duplicateSelectionItem.IsEnabled = deleteSelectionItem.IsEnabled = !busy && HasSelection;
            try { pasteSelectionItem.IsEnabled = !busy && session is not null && CanPastePageContent(); }
            catch (ExternalException) { pasteSelectionItem.IsEnabled = false; }
        };
        InkSurface.ContextMenu = selectionMenu;
        SelectionActionsButton.ContextMenu = selectionMenu;
        InkSurface.ContextMenuOpening += (_, e) =>
        {
            // Typed objects retain their own native editing/delete menu.
            for (var node = e.OriginalSource as DependencyObject; node is not null && node != InkSurface; node = VisualTreeHelper.GetParent(node))
                if (node is TextBox) return;
            e.Handled = true;
            if (barrelSelection && !HasSelection) return;
            if (!busy) OpenSelectionMenu(Mouse.GetPosition(InkSurface));
        };
        InkSurface.SelectionChanged += (_, _) => { selectionMenu.IsOpen = false; UpdateSelectionActions(); };
        InkSurface.SelectionMoved += (_, _) => UpdateSelectionActions();
        InkSurface.SelectionResized += (_, _) => UpdateSelectionActions();
        Viewport.ViewChanged += () => { selectionMenu.IsOpen = false; UpdateSelectionActions(); };
        MainStage.SizeChanged += (_, _) => UpdateSelectionActions();
        Viewport.IsSelectionTapTarget = point => IsSelectionInterior(Viewport.TranslatePoint(point, InkSurface));
        Viewport.TouchHeld += point =>
        {
            if (!ready || busy || homeVisible) return;
            var pagePoint = Viewport.TranslatePoint(point, InkSurface);
            if (pagePoint.X >= 0 && pagePoint.Y >= 0 && pagePoint.X <= InkSurface.Width && pagePoint.Y <= InkSurface.Height)
                OpenSelectionMenu(pagePoint);
        };
        Viewport.SelectionTapped += point => OpenSelectionMenu(Viewport.TranslatePoint(point, InkSurface));
        Viewport.AddHandler(PreviewMouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            selectionPointerDown = null;
            if (e.ChangedButton != MouseButton.Left || e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch ||
                !IsSelectionInterior(e.GetPosition(InkSurface))) return;
            selectionPointerDown = e.GetPosition(Viewport); selectionPointerPage = currentPageId;
        }), true);
        Viewport.AddHandler(PreviewMouseMoveEvent, new MouseEventHandler((_, e) =>
        {
            if (selectionPointerDown is Point origin && (e.GetPosition(Viewport) - origin).Length > 8) selectionPointerDown = null;
        }), true);
        Viewport.AddHandler(PreviewMouseUpEvent, new MouseButtonEventHandler((_, e) =>
        {
            var down = selectionPointerDown; selectionPointerDown = null;
            if (down is null || selectionPointerPage != currentPageId || e.ChangedButton != MouseButton.Left ||
                (e.GetPosition(Viewport) - down.Value).Length > 8) return;
            var point = e.GetPosition(InkSurface); var page = currentPageId;
            Dispatcher.BeginInvoke(() => { if (page == currentPageId && IsSelectionInterior(point)) OpenSelectionMenu(point); }, DispatcherPriority.Input);
        }), true);
    }
    private void ClearPageSelection()
    {
        selectionPointerDown = null; selectionPointerPage = null;
        selectionMenu.IsOpen = false;
        // InkCanvas.Select switches EditingMode to Select even for an empty
        // selection. Clearing transient state must not change the active tool.
        if (HasSelection)
        {
            var mode = InkSurface.EditingMode;
            try { InkSurface.Select(new StrokeCollection(), Array.Empty<UIElement>()); }
            finally { InkSurface.EditingMode = mode; }
        }
        foreach (var box in InkSurface.Children.OfType<TextBox>()) box.Select(0, 0);
    }

    private bool IsSelectionInterior(Point point) => !busy && EffectiveLasso && HasSelection &&
        InkSurface.HitTestSelection(point) == InkCanvasSelectionHitResult.Selection;

    private void UpdateSelectionActions()
    {
        UpdateCalculationActions();
        if (SelectionActionsButton is null) return;
        var visible = !busy && !restoring && !homeVisible && EffectiveLasso && HasSelection;
        SelectionActionsButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible) return;
        var count = InkSurface.GetSelectedStrokes().Count + InkSurface.GetSelectedElements().Count;
        SelectionCount.Text = $"{count} selected";
        var bounds = InkSurface.TransformToAncestor(MainStage).TransformBounds(InkSurface.GetSelectionBounds());
        SelectionActionsButton.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var size = SelectionActionsButton.DesiredSize;
        var x = Math.Clamp(bounds.Left, 8, Math.Max(8, MainStage.ActualWidth - size.Width - 8));
        var y = bounds.Bottom + 12;
        if (y + size.Height > MainStage.ActualHeight - 8) y = bounds.Top - size.Height - 12;
        y = Math.Clamp(y, 8, Math.Max(8, MainStage.ActualHeight - size.Height - 8));
        SelectionActionsButton.RenderTransform = new TranslateTransform(x, y);
    }
    private void SelectionActions_Click(object sender, RoutedEventArgs e)
    {
        var bounds = InkSurface.GetSelectionBounds();
        if (!bounds.IsEmpty) OpenSelectionMenu(new Point(bounds.Left, bounds.Bottom));
    }
    private void OpenSelectionMenu(Point point)
    {
        if (busy || session is null) return;
        CloseToolInspector();
        selectionMenuPoint = point;
        var position = InkSurface.TranslatePoint(point, MainStage);
        selectionMenu.PlacementTarget = MainStage; selectionMenu.Placement = PlacementMode.RelativePoint;
        selectionMenu.HorizontalOffset = position.X; selectionMenu.VerticalOffset = position.Y;
        selectionMenu.IsOpen = true;
    }
    private void RunSelectionAction(Action action)
    {
        selectionMenu.IsOpen = false;
        try { action(); }
        catch (Exception ex) when (ex is ExternalException or IOException or JsonException or InvalidOperationException or NotSupportedException or ArgumentException)
        { model.Status = ex is ExternalException ? "Clipboard is busy. Try copying or pasting again." : ex.Message; }
    }
    private PageDocument? CaptureSelectedObjects()
    {
        if (session is null || !HasSelection) return null;
        var bounds = InkSurface.GetSelectionBounds();
        var strokes = InkSurface.GetSelectedStrokes().Clone();
        strokes.Transform(new Matrix(1, 0, 0, 1, -bounds.Left, -bounds.Top), false);
        var ids = InkSurface.GetSelectedElements().OfType<TextBox>().Select(box => (Guid)box.Tag).ToHashSet();
        var texts = session.Texts.Where(t => ids.Contains(t.Id)).Select(t => t with { X = t.X - bounds.Left, Y = t.Y - bounds.Top }).ToList();
        return InkMapper.Capture(strokes) with { Width = session.Paper.Width, Height = session.Paper.Height, Paper = session.Paper, Texts = texts, Images = session.Images.Where(i => InkSurface.GetSelectedElements().OfType<Image>().Any(e => (Guid)e.Tag == i.Id)).Select(i => i with { X = i.X - bounds.Left, Y = i.Y - bounds.Top }).ToList() };
    }
    public void CopySelectedObjects()
    {
        if (busy || CaptureSelectedObjects() is not { } document) return;
        var data = new DataObject();
        data.SetData(SelectionClipboardFormat, JsonSerializer.Serialize(document));
        using var ink = new MemoryStream();
        if (document.Strokes.Count > 0)
        {
            InkMapper.Restore(document).Save(ink); ink.Position = 0;
            data.SetData(StrokeCollection.InkSerializedFormat, ink);
        }
        if (document.Texts is { Count: > 0 }) data.SetText(string.Join(Environment.NewLine, document.Texts.Select(t => t.Text)));
        Clipboard.SetDataObject(data, copy: true);
        model.Status = "Selection copied · paste on any page with Ctrl+V or the lasso menu";
    }
    public void DuplicateSelectedObjects()
    {
        if (busy || CaptureSelectedObjects() is not { } document) return;
        var bounds = InkSurface.GetSelectionBounds();
        InsertSelection(document, new Point(bounds.Left + 24, bounds.Top + 24));
    }
    public void DeleteSelectedObjects()
    {
        if (busy || session is null || !HasSelection) return;
        var selectedImageIds = InkSurface.GetSelectedElements().OfType<Image>().Select(i => (Guid)i.Tag).ToHashSet();
        var strokes = InkSurface.GetSelectedStrokes();
        var ids = InkSurface.GetSelectedElements().OfType<TextBox>().Select(box => (Guid)box.Tag).ToHashSet();
        restoring = true;
        try
        {
            InkSurface.Select(new StrokeCollection(), Array.Empty<UIElement>());
            if (strokes.Count > 0) InkSurface.Strokes.Remove(strokes);
            session.Texts.RemoveAll(t => ids.Contains(t.Id));
            session.Images.RemoveAll(i => selectedImageIds.Contains(i.Id)); RenderTexts();
        }
        finally { restoring = false; }
        SaveFullPage(); UpdateSelectionActions();
    }
    public void PasteSelectedObjects(Point? position = null)
    {
        if (busy || session is null) return;
        CommitTextEditor();
        if (Clipboard.GetData(SelectionClipboardFormat) is not string json)
        {
            PasteClipboardImage(position); return;
        }
        if (json.Length > 64 * 1024 * 1024) throw new InvalidDataException("This selection is too large to paste.");
        var document = JsonSerializer.Deserialize<PageDocument>(json) ?? throw new InvalidDataException("The copied selection is invalid.");
        document.Validate();
        var center = Viewport.TranslatePoint(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), InkSurface);
        InsertSelection(document, position ?? center);
    }
    private void InsertSelection(PageDocument document, Point position)
    {
        if (session is null || document.Strokes.Count + (document.Texts?.Count ?? 0) + (document.Images?.Count ?? 0) == 0) return;
        if (session.Texts.Count + (document.Texts?.Count ?? 0) > 2000) throw new InvalidOperationException("This page has too many text objects to paste more.");
        if (session.Images.Count + (document.Images?.Count ?? 0) > 200 ||
            session.Images.Sum(i => (long)i.Png.Length) + (document.Images?.Sum(i => (long)i.Png.Length) ?? 0) > 32_000_000)
            throw new InvalidOperationException("This page has too many images to paste more.");
        var strokes = InkMapper.Restore(document);
        var bounds = strokes.GetBounds();
        foreach (var text in document.Texts ?? [])
            bounds.Union(new Rect(text.X, text.Y, text.Width, text.FontSize * 1.5));
        foreach (var image in document.Images ?? []) bounds.Union(new Rect(image.X, image.Y, image.Width, image.Height));
        var x = Math.Clamp(position.X, 0, Math.Max(0, session.Paper.Width - bounds.Right));
        var y = Math.Clamp(position.Y, 0, Math.Max(0, session.Paper.Height - bounds.Bottom));
        strokes.Transform(new Matrix(1, 0, 0, 1, x, y), false);
        foreach (var stroke in strokes) InkMapper.RenewIdentity(stroke);
        var texts = (document.Texts ?? []).Select(t => t with { Id = Guid.NewGuid(), X = t.X + x, Y = t.Y + y }).ToList();
        var images = (document.Images ?? []).Select(i => i with { Id = Guid.NewGuid(), X = i.X + x, Y = i.Y + y }).ToList();
        restoring = true;
        try { InkSurface.Strokes.Add(strokes); session.Texts.AddRange(texts); session.Images.AddRange(images); RenderTexts(); }
        finally { restoring = false; }
        var ids = texts.Select(t => t.Id).Concat(images.Select(i => i.Id)).ToHashSet();
        LassoButton.IsChecked = true;
        InkSurface.Select(strokes, InkSurface.Children.OfType<FrameworkElement>().Where(box => box.Tag is Guid id && ids.Contains(id)).Cast<UIElement>());
        SaveFullPage(); UpdateSelectionActions();
    }
}
