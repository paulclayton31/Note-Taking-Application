using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Input.StylusPlugIns;
using System.Collections.Concurrent;

namespace SurfaceNotebook.Controls;

public sealed class PenCanvas : InkCanvas
{
    private sealed class PenRenderer : DynamicRenderer
    {
        private readonly ConcurrentDictionary<int, byte> pens = new();
        public void Remember(int id) => pens.TryAdd(id, 0);
        protected override void OnStylusDown(RawStylusInput input) { if (pens.ContainsKey(input.StylusDeviceId)) base.OnStylusDown(input); }
        protected override void OnStylusMove(RawStylusInput input) { if (pens.ContainsKey(input.StylusDeviceId)) base.OnStylusMove(input); }
        protected override void OnStylusUp(RawStylusInput input) { if (pens.ContainsKey(input.StylusDeviceId)) base.OnStylusUp(input); }
    }
    private readonly PenRenderer penRenderer = new();
    public PenCanvas()
    {
        DynamicRenderer = penRenderer;
        Loaded += (_, _) =>
        {
            foreach (TabletDevice tablet in Tablet.TabletDevices)
                if (tablet.Type == TabletDeviceType.Stylus)
                    foreach (StylusDevice pen in tablet.StylusDevices) penRenderer.Remember(pen.Id);
        };
    }
    protected override void OnStylusInRange(StylusEventArgs e)
    {
        if (AcceptsInk(e.StylusDevice.TabletDevice.Type)) penRenderer.Remember(e.StylusDevice.Id);
        base.OnStylusInRange(e);
    }
    // No setting enables touch or promoted mouse ink. Only a real pen is accepted.
    public static bool AcceptsInk(TabletDeviceType? deviceType) => deviceType == TabletDeviceType.Stylus;

    protected override void OnPreviewStylusDown(StylusDownEventArgs e)
    {
        if (!AcceptsInk(e.StylusDevice.TabletDevice.Type))
        {
            e.Handled = true;
            return;
        }
        penRenderer.Remember(e.StylusDevice.Id);
        base.OnPreviewStylusDown(e);
    }

    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        // Normally intercepted by PageViewport. Never promote a touch into ink.
        e.Handled = true;
    }

    protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
    {
        if (e.StylusDevice is null && EditingMode is InkCanvasEditingMode.Select or InkCanvasEditingMode.None)
        { base.OnPreviewMouseDown(e); return; }
        if (!AcceptsInk(e.StylusDevice?.TabletDevice.Type)) e.Handled = true;
        else base.OnPreviewMouseDown(e);
    }
}
