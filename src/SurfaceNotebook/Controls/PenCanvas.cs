using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SurfaceNotebook.Controls;

public sealed class PenCanvas : InkCanvas
{
    public bool AllowMouseDrawing { get; set; }

    protected override void OnPreviewStylusDown(StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch)
        {
            e.Handled = true;
            return;
        }
        base.OnPreviewStylusDown(e);
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        // Explicit pen-only policy: palms/fingers must never create strokes.
        // Touch navigation will be implemented and hardware-tested separately.
        e.Handled = true;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice?.TabletDevice.Type == TabletDeviceType.Touch ||
            (e.StylusDevice is null && !AllowMouseDrawing)) e.Handled = true;
        else base.OnPreviewMouseDown(e);
    }
}
