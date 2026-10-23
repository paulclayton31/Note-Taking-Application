using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Controls;

public sealed class PageViewport : Grid
{
    private readonly Dictionary<int, Point> contacts = [];
    private bool penInRange;
    public PageView View { get; private set; } = new(1, 24, 24);
    public event Action? ViewChanged;
    public event Action? GestureCompleted;
    public bool PenInRange => penInRange;

    public PageViewport()
    {
        ClipToBounds = true;
        Background = new SolidColorBrush(Color.FromRgb(238, 241, 245));
        // Handle at the ancestor preview stage, before InkCanvas's device handlers.
        PreviewStylusDown += HandleStylusDown;
        PreviewStylusMove += HandleStylusMove;
        PreviewStylusUp += HandleStylusUp;
        StylusInRange += (_, e) => { if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus) SetPenPresence(true); };
        StylusOutOfRange += (_, e) => { if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus) SetPenPresence(false); };
        StylusLeave += (_, e) => { if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus && e.StylusDevice.InAir) SetPenPresence(false); };
        PreviewTouchDown += (_, e) =>
        {
            BeginContact(e.TouchDevice.Id, e.GetTouchPoint(this).Position);
            e.Handled = true;
            if (e.TouchDevice.IsActive) e.TouchDevice.Capture(this);
        };
        PreviewTouchMove += (_, e) => { MoveContact(e.TouchDevice.Id, e.GetTouchPoint(this).Position); e.Handled = true; };
        PreviewTouchUp += (_, e) => { EndContact(e.TouchDevice.Id); e.Handled = true; if (e.TouchDevice.IsActive) e.TouchDevice.Capture(null); };
        LostTouchCapture += (_, e) => EndContact(e.TouchDevice.Id);
        LostStylusCapture += (_, e) => { if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Touch) EndContact(e.StylusDevice.Id); };
        PreviewMouseWheel += (_, e) =>
        {
            if (penInRange) { e.Handled = true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Zoom(Math.Pow(1.15, e.Delta / 120.0), e.GetPosition(this));
            else SetView(View.Pan(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? e.Delta : 0,
                Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0 : e.Delta));
            GestureCompleted?.Invoke();
            e.Handled = true;
        };
    }

    public void SetPenPresence(bool present)
    {
        penInRange = present;
        contacts.Clear();
    }
    private void HandleStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus) { SetPenPresence(true); return; }
        e.Handled = true;
        BeginContact(e.StylusDevice.Id, e.GetPosition(this));
        e.StylusDevice.Capture(this);
    }
    private void HandleStylusMove(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Touch) return;
        e.Handled = true;
        MoveContact(e.StylusDevice.Id, e.GetPosition(this));
    }
    private void HandleStylusUp(object sender, StylusEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Touch) return;
        e.Handled = true;
        EndContact(e.StylusDevice.Id);
        e.StylusDevice.Capture(null);
    }

    public void BeginContact(int id, Point position)
    {
        if (!penInRange) contacts[id] = position;
    }
    public void MoveContact(int id, Point position)
    {
        if (penInRange || !contacts.ContainsKey(id)) return;
        var before = contacts.OrderBy(p => p.Key).Take(2).Select(p => p.Value).ToArray();
        contacts[id] = position;
        var after = contacts.OrderBy(p => p.Key).Take(2).Select(p => p.Value).ToArray();
        if (before.Length == 1) SetView(View.Pan(after[0].X - before[0].X, after[0].Y - before[0].Y));
        else
        {
            var oldCenter = new Point((before[0].X + before[1].X) / 2, (before[0].Y + before[1].Y) / 2);
            var newCenter = new Point((after[0].X + after[1].X) / 2, (after[0].Y + after[1].Y) / 2);
            var oldDistance = (before[1] - before[0]).Length;
            var newDistance = (after[1] - after[0]).Length;
            var scaled = View.ZoomAt(oldDistance > 8 ? newDistance / oldDistance : 1, oldCenter.X, oldCenter.Y);
            SetView(scaled.Pan(newCenter.X - oldCenter.X, newCenter.Y - oldCenter.Y));
        }
    }
    public void EndContact(int id)
    {
        if (contacts.Remove(id) && contacts.Count == 0) GestureCompleted?.Invoke();
    }
    public void SetView(PageView view)
    {
        View = view.Constrain(ActualWidth > 0 ? ActualWidth : 1000, ActualHeight > 0 ? ActualHeight : 700);
        if (Children.Count > 0)
            Children[0].RenderTransform = new MatrixTransform(View.Zoom, 0, 0, View.Zoom, View.X, View.Y);
        ViewChanged?.Invoke();
    }
    public void Zoom(double factor, Point anchor)
    {
        if (!penInRange) SetView(View.ZoomAt(factor, anchor.X, anchor.Y));
    }
    public void FitPage()
    {
        var width = ActualWidth > 0 ? ActualWidth : 1000;
        var zoom = Math.Clamp((width - 48) / PageDocument.DefaultWidth, PageView.MinZoom, 1.5);
        SetView(new PageView(zoom, (width - PageDocument.DefaultWidth * zoom) / 2, 24));
        GestureCompleted?.Invoke();
    }
    public void CompleteGesture() => GestureCompleted?.Invoke();
}
