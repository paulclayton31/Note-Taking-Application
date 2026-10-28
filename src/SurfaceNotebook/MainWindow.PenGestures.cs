using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly HoldShapeGesture heldShape = new();
    private readonly DispatcherTimer holdShapeTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    private StylusDevice? shapePen;
    private ShapePreview? shapePreview;
    private AdornerLayer? shapePreviewLayer;
    private bool barrelSelection;
    private bool EffectiveLasso => toolName == "Lasso" || barrelSelection;

    private sealed class ShapePreview(UIElement element) : Adorner(element)
    {
        public Stroke? Stroke { get; set; }
        protected override void OnRender(DrawingContext context) => Stroke?.Draw(context);
    }
    private static bool BarrelPressed(StylusDevice pen) => pen.StylusButtons.Cast<StylusButton>()
        .Any(b => b.Guid == StylusPointProperties.BarrelButton.Id && b.StylusButtonState == StylusButtonState.Down);

    private void InitializePenGestures()
    {
        Stylus.SetIsPressAndHoldEnabled(InkSurface, false);
        Stylus.SetIsFlicksEnabled(InkSurface, false);
        AddHandler(PreviewStylusButtonDownEvent, new StylusButtonEventHandler((_, e) =>
        {
            if (e.StylusButton.Guid == StylusPointProperties.BarrelButton.Id && e.StylusDevice.InAir &&
                !e.StylusDevice.Inverted && !busy && !homeVisible && session is not null && IsInkDescendant(e.OriginalSource as DependencyObject))
                SetBarrelSelection(true);
        }), true);
        AddHandler(PreviewStylusButtonUpEvent, new StylusButtonEventHandler((_, e) =>
        {
            if (e.StylusButton.Guid == StylusPointProperties.BarrelButton.Id && e.StylusDevice.InAir && !HasSelection)
                SetBarrelSelection(false);
        }), true);
        AddHandler(StylusOutOfRangeEvent, new StylusEventHandler((_, e) =>
        {
            if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus) return;
            CancelHeldShape();
            if (!HasSelection) SetBarrelSelection(false);
        }), true);
        // The window preview runs before PageViewport's hand/pan handler.
        AddHandler(PreviewStylusDownEvent, new StylusDownEventHandler((_, e) =>
        {
            if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus || busy || homeVisible || session is null) return;
            var point = e.GetPosition(InkSurface);
            if (!IsInkDescendant(e.OriginalSource as DependencyObject)) return;
            CancelHeldShape();
            var keepSelection = barrelSelection && HasSelection && InkSurface.HitTestSelection(point) != InkCanvasSelectionHitResult.None;
            SetBarrelSelection(!e.StylusDevice.Inverted && (BarrelPressed(e.StylusDevice) || keepSelection));
            if (toolName != "Pen" || barrelSelection || e.StylusDevice.Inverted || HoldShapesToggle.IsChecked != true) return;
            shapePen = e.StylusDevice;
            heldShape.Begin(InkSurface.DefaultDrawingAttributes, point, e.GetPosition(this), Environment.TickCount64);
            holdShapeTimer.Start();
        }), true);
        AddHandler(PreviewStylusMoveEvent, new StylusEventHandler((_, e) =>
        {
            if (shapePen != e.StylusDevice || !heldShape.Active || e.StylusDevice.InAir) return;
            foreach (var p in e.GetStylusPoints(InkSurface))
                heldShape.Move(p.ToPoint(), InkSurface.TranslatePoint(p.ToPoint(), this), Environment.TickCount64);
            if (heldShape.Result is null && shapePreview is not null)
            {
                RemoveShapePreview();
                InkSurface.ResumeInkPreview(e.StylusDevice, heldShape.PreviewPoints());
                InputHint.Text = ToolHint.Text;
            }
        }), true);
        AddHandler(PreviewStylusUpEvent, new StylusEventHandler((_, e) =>
        {
            if (shapePen == e.StylusDevice)
            {
                heldShape.Check(Environment.TickCount64); holdShapeTimer.Stop();
                // StrokeCollected runs after this preview. Clear only afterwards.
                Dispatcher.BeginInvoke(CancelHeldShape, DispatcherPriority.Background);
            }
            if (barrelSelection)
                Dispatcher.BeginInvoke(() => { if (!HasSelection) SetBarrelSelection(false); }, DispatcherPriority.Background);
        }), true);
        holdShapeTimer.Tick += (_, _) =>
        {
            if (shapePen is null || shapePen.InAir || shapePen.Inverted || busy || homeVisible || toolName != "Pen") { CancelHeldShape(); return; }
            if (heldShape.Check(Environment.TickCount64) is not { } result) return;
            shapePreviewLayer ??= AdornerLayer.GetAdornerLayer(InkSurface);
            if (shapePreviewLayer is null) return;
            if (shapePreview is null)
            {
                shapePreview = new ShapePreview(InkSurface) { IsHitTestVisible = false };
                shapePreviewLayer.Add(shapePreview);
            }
            shapePreview.Stroke = result.Stroke; shapePreview.InvalidateVisual();
            InkSurface.ShowShapePreview(true);
            InputHint.Text = $"{result.Name} · lift to finish · Undo restores your drawing";
        };
        InkSurface.LostStylusCapture += (_, _) =>
        {
            // WPF releases its capture before StrokeCollected; defer cleanup.
            Dispatcher.BeginInvoke(CancelHeldShape, DispatcherPriority.Background);
        };
        Deactivated += (_, _) => { CancelHeldShape(); SetBarrelSelection(false); };
        Closed += (_, _) => CancelHeldShape();
    }
    private bool IsInkDescendant(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node == InkSurface) return true;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
    private void RemoveShapePreview()
    {
        if (shapePreview is not null) shapePreviewLayer?.Remove(shapePreview);
        shapePreview = null; shapePreviewLayer = null; InkSurface.ShowShapePreview(false);
    }
    private void CancelHeldShape()
    {
        holdShapeTimer.Stop(); shapePen = null; heldShape.Cancel(); RemoveShapePreview();
        if (InputHint is not null && ToolHint is not null) InputHint.Text = ToolHint.Text;
    }
    private bool TryFinishHeldShape(Stroke original)
    {
        var result = heldShape.Result;
        CancelHeldShape();
        if (result is null || session is null || !InkSurface.Strokes.Contains(original)) return false;
        // The original stroke is already in history. Undo restores it, and a
        // second Undo removes it, just like the undoable scratch-out operation.
        restoring = true;
        try { InkSurface.Strokes.Remove(original); InkSurface.Strokes.Add(result.Stroke); }
        finally { restoring = false; }
        SaveFullPage();
        return true;
    }
    public void SetBarrelSelection(bool active)
    {
        if (barrelSelection == active) return;
        CancelHeldShape();
        if (active) CommitTextEditor();
        barrelSelection = active;
        ApplyPen();
        UpdateTextInputModes();
        UpdateSelectionActions();
        InputHint.Text = active ? "Side-button lasso · circle objects · tap the selection for actions" : ToolHint.Text;
    }
}
