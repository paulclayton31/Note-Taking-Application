using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly TranslateTransform toolbarTranslation = new();
    private ToolbarPosition toolbarPosition = new();
    private int? toolbarTouchId;
    private StylusDevice? toolbarPen;
    private Point toolbarPointerPrevious;

    private void InitializeToolbarMovement()
    {
        ToolbarFrame.RenderTransform = toolbarTranslation;
        MainStage.SizeChanged += (_, _) => ApplyToolbarPosition();
        ToolbarFrame.SizeChanged += (_, _) => ApplyToolbarPosition();
        ToolbarGrip.PreviewTouchDown += (_, e) =>
        {
            e.Handled = true;
            if (toolbarTouchId is not null || toolbarPen is not null) return;
            CloseToolInspector();
            toolbarTouchId = e.TouchDevice.Id; toolbarPointerPrevious = e.GetTouchPoint(MainStage).Position;
            if (e.TouchDevice.IsActive) e.TouchDevice.Capture(ToolbarGrip);
        };
        ToolbarGrip.PreviewTouchMove += (_, e) =>
        {
            e.Handled = true;
            if (toolbarTouchId != e.TouchDevice.Id) return;
            MoveToolbarPointer(e.GetTouchPoint(MainStage).Position);
        };
        ToolbarGrip.PreviewTouchUp += (_, e) =>
        {
            e.Handled = true;
            if (toolbarTouchId != e.TouchDevice.Id) return;
            toolbarTouchId = null;
            if (e.TouchDevice.IsActive) e.TouchDevice.Capture(null);
            SavePreferences();
        };
        ToolbarGrip.LostTouchCapture += (_, e) =>
        {
            if (toolbarTouchId != e.TouchDevice.Id) return;
            toolbarTouchId = null; SavePreferences();
        };
        ToolbarGrip.PreviewStylusDown += (_, e) =>
        {
            if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Stylus) return;
            e.Handled = true;
            if (toolbarTouchId is not null || toolbarPen is not null) return;
            CloseToolInspector(); toolbarPen = e.StylusDevice; toolbarPointerPrevious = e.GetPosition(MainStage);
            e.StylusDevice.Capture(ToolbarGrip);
        };
        ToolbarGrip.PreviewStylusMove += (_, e) =>
        {
            if (toolbarPen != e.StylusDevice) return;
            e.Handled = true; MoveToolbarPointer(e.GetPosition(MainStage));
        };
        ToolbarGrip.PreviewStylusUp += (_, e) =>
        {
            if (toolbarPen != e.StylusDevice) return;
            e.Handled = true; toolbarPen = null; e.StylusDevice.Capture(null); SavePreferences();
        };
        ToolbarGrip.LostStylusCapture += (_, e) =>
        {
            if (toolbarPen != e.StylusDevice) return;
            toolbarPen = null; SavePreferences();
        };
    }
    private void RestoreToolbarPosition()
    {
        toolbarPosition = (preferences.ToolbarPosition ?? new ToolbarPosition(.5, preferences.ToolbarBottom ? 1 : 0)).Constrain();
        ApplyToolbarPosition();
    }
    private (double X, double Y) ToolbarTravel => (
        Math.Max(0, MainStage.ActualWidth - ToolbarFrame.ActualWidth - 24),
        Math.Max(0, MainStage.ActualHeight - ToolbarFrame.ActualHeight - 24));
    private void ApplyToolbarPosition()
    {
        if (MainStage.ActualWidth > 0) ToolbarFrame.MaxWidth = Math.Max(64, MainStage.ActualWidth - 24);
        SavedColorBar.MaxWidth = Math.Max(100, MainStage.ActualWidth - 24);
        SavedColorBar.Margin = new Thickness(12, 12, 12, toolbarPosition.Vertical > .7 ? ToolbarFrame.ActualHeight + 24 : 12);
        var travel = ToolbarTravel;
        toolbarTranslation.X = travel.X * toolbarPosition.Horizontal;
        toolbarTranslation.Y = travel.Y * toolbarPosition.Vertical;
    }
    private void Toolbar_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (ToolbarScroll.ScrollableWidth <= 0) return;
        ToolbarScroll.ScrollToHorizontalOffset(ToolbarScroll.HorizontalOffset - e.Delta);
        e.Handled = true;
    }
    private void MoveToolbarPointer(Point point)
    {
        MoveToolbarBy(point.X - toolbarPointerPrevious.X, point.Y - toolbarPointerPrevious.Y);
        toolbarPointerPrevious = point;
    }
    private void MoveToolbarBy(double x, double y)
    {
        CloseToolInspector();
        var travel = ToolbarTravel;
        toolbarPosition = new ToolbarPosition(
            travel.X > 0 ? Math.Clamp(toolbarTranslation.X + x, 0, travel.X) / travel.X : toolbarPosition.Horizontal,
            travel.Y > 0 ? Math.Clamp(toolbarTranslation.Y + y, 0, travel.Y) / travel.Y : toolbarPosition.Vertical);
        ApplyToolbarPosition();
    }
    private void SetToolbarDock(bool bottom)
    {
        toolbarPosition = new ToolbarPosition(.5, bottom ? 1 : 0);
        preferences = preferences with { ToolbarBottom = bottom };
        ApplyToolbarPosition(); SavePreferences(); CloseToolInspector();
    }
    private void DockToolbarTop_Click(object sender, RoutedEventArgs e) => SetToolbarDock(false);
    private void DockToolbarBottom_Click(object sender, RoutedEventArgs e) => SetToolbarDock(true);
    private void Toolbar_DragDelta(object sender, DragDeltaEventArgs e) => MoveToolbarBy(e.HorizontalChange, e.VerticalChange);
    private void Toolbar_DragCompleted(object sender, DragCompletedEventArgs e) { CloseToolInspector(); SavePreferences(); }
    private void ToolbarGrip_KeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers is not ModifierKeys.None and not ModifierKeys.Shift) return;
        var step = Keyboard.Modifiers == ModifierKeys.Shift ? 2 : 16;
        switch (e.Key)
        {
            case Key.Left: MoveToolbarBy(-step, 0); break;
            case Key.Right: MoveToolbarBy(step, 0); break;
            case Key.Up: MoveToolbarBy(0, -step); break;
            case Key.Down: MoveToolbarBy(0, step); break;
            default: return;
        }
        e.Handled = true; SavePreferences();
    }
}
