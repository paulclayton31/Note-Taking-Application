using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private Point? calculationAreaStart;
    private Guid? calculationAreaPage;
    private void SelectCalculationArea_Click(object sender, RoutedEventArgs e)
    {
        if (!CalculationMode || busy || session is null) return;
        CommitTextEditor(); ClearPageSelection(); CloseToolInspector();
        CalculationAreaOverlay.Visibility = Visibility.Visible;
        calculationAreaPage = currentPageId;
        model.Status = "Drag around a problem with your pen or mouse · Esc to cancel";
    }
    private void CancelCalculationArea()
    {
        calculationAreaStart = null; calculationAreaPage = null;
        if (CalculationAreaOverlay is null) return;
        CalculationAreaOverlay.Visibility = Visibility.Collapsed;
        CalculationAreaOutline.Visibility = Visibility.Collapsed;
        if (CalculationAreaOverlay.IsMouseCaptured) CalculationAreaOverlay.ReleaseMouseCapture();
    }
    private Point CalculationAreaPoint(MouseEventArgs e)
    {
        var point = e.GetPosition(CalculationAreaOverlay);
        return new Point(Math.Clamp(point.X, 0, CalculationAreaOverlay.ActualWidth), Math.Clamp(point.Y, 0, CalculationAreaOverlay.ActualHeight));
    }
    private void CalculationArea_Down(object sender, MouseButtonEventArgs e)
    {
        if (e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch || !CalculationMode || busy) return;
        calculationAreaStart = CalculationAreaPoint(e); CalculationAreaOverlay.CaptureMouse();
        CalculationAreaOutline.Visibility = Visibility.Visible;
        Canvas.SetLeft(CalculationAreaOutline, calculationAreaStart.Value.X); Canvas.SetTop(CalculationAreaOutline, calculationAreaStart.Value.Y);
        CalculationAreaOutline.Width = CalculationAreaOutline.Height = 0; e.Handled = true;
    }
    private void CalculationArea_Move(object sender, MouseEventArgs e)
    {
        if (calculationAreaStart is not Point start) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelCalculationArea(); return; }
        var bounds = new Rect(start, CalculationAreaPoint(e));
        Canvas.SetLeft(CalculationAreaOutline, bounds.Left); Canvas.SetTop(CalculationAreaOutline, bounds.Top);
        CalculationAreaOutline.Width = bounds.Width; CalculationAreaOutline.Height = bounds.Height; e.Handled = true;
    }
    private void CalculationArea_Up(object sender, MouseButtonEventArgs e)
    {
        if (calculationAreaStart is not Point start) return;
        var bounds = new Rect(start, CalculationAreaPoint(e)); var page = calculationAreaPage;
        CancelCalculationArea(); e.Handled = true;
        if (page != currentPageId || bounds.Width < 8 || bounds.Height < 8) return;
        Dispatcher.BeginInvoke(() => { if (page == currentPageId && CalculationMode) OpenCalculationEditor(false, region: bounds); });
    }
    private void CalculationArea_LostCapture(object sender, MouseEventArgs e)
    { if (calculationAreaStart is not null) CancelCalculationArea(); }
}
