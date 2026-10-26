using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SurfaceNotebook.Models;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private bool updatingZoom;
    private void UpdateZoomControls()
    {
        if (ZoomLabel is null || ZoomSlider is null) return;
        updatingZoom = true;
        try
        {
            ZoomLabel.Text = $"{Viewport.View.Zoom:P0}";
            ZoomValueLabel.Text = ZoomLabel.Text;
            ZoomSlider.Value = Viewport.View.Zoom * 100;
        }
        finally { updatingZoom = false; }
    }
    public void SetZoomPercent(double percent)
    {
        if (!double.IsFinite(percent)) return;
        var zoom = Math.Clamp(percent / 100, PageView.MinZoom, PageView.MaxZoom);
        Viewport.SetView(Viewport.View.ZoomAt(zoom / Viewport.View.Zoom, Viewport.ActualWidth / 2, Viewport.ActualHeight / 2));
        Viewport.CompleteGesture();
    }
    private void ZoomMenu_Click(object sender, RoutedEventArgs e)
    {
        CloseToolInspector(); selectionMenu.IsOpen = false; UpdateZoomControls(); ZoomPopup.IsOpen = !ZoomPopup.IsOpen;
    }
    private void ZoomSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!updatingZoom && ready && !busy) SetZoomPercent(e.NewValue); }
    private void ZoomPreset_Click(object sender, RoutedEventArgs e)
    {
        SetZoomPercent(double.Parse((string)((Button)sender).Tag, CultureInfo.InvariantCulture));
        ZoomPopup.IsOpen = false;
    }
}
