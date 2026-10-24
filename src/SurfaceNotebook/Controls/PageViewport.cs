using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Controls;

public sealed class PageViewport : Canvas
{
    private readonly Dictionary<int, Point> contacts = [];
    private bool penInRange;
    private Point? dragPoint;
    private double scrollBeyondEdge;
    private bool pinching;
    public double PaperWidth { get; private set; } = PageDocument.DefaultWidth;
    public double PaperHeight { get; private set; } = PageDocument.DefaultHeight;
    public bool HandMode { get; set; }
    public PageView View { get; private set; } = new(1, 24, 24);
    public event Action? ViewChanged;
    public event Action? GestureCompleted;
    public event Action<int>? PageTurnRequested;
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
        PreviewMouseDown += (_, e) =>
        {
            if (!HandMode || e.StylusDevice is not null || e.ChangedButton != MouseButton.Left) return;
            dragPoint = e.GetPosition(this); CaptureMouse(); e.Handled = true;
        };
        PreviewMouseMove += (_, e) =>
        {
            if (!HandMode || e.StylusDevice is not null || dragPoint is not Point old || e.LeftButton != MouseButtonState.Pressed) return;
            var point = e.GetPosition(this); SetView(View.Pan(point.X - old.X, point.Y - old.Y)); dragPoint = point; e.Handled = true;
        };
        PreviewMouseUp += (_, e) =>
        {
            if (e.StylusDevice is not null || dragPoint is null) return;
            dragPoint = null; ReleaseMouseCapture(); CompleteGesture(); e.Handled = true;
        };
        LostMouseCapture += (_, _) => dragPoint = null;
        PreviewMouseWheel += (_, e) =>
        {
            if (penInRange) { e.Handled = true; return; }
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) Zoom(Math.Pow(1.15, e.Delta / 120.0), e.GetPosition(this));
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) SetView(View.Pan(e.Delta, 0));
            else
            {
                TrackEdgeScroll(e.Delta);
                SetView(View.Pan(0, e.Delta));
                TurnAtEdge();
            }
            GestureCompleted?.Invoke();
            e.Handled = true;
        };
    }

    public void SetPenPresence(bool present)
    {
        penInRange = present;
        contacts.Clear();
        scrollBeyondEdge = 0;
    }
    private void HandleStylusDown(object sender, StylusDownEventArgs e)
    {
        if (e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus)
        {
            SetPenPresence(true);
            if (HandMode) { dragPoint = e.GetPosition(this); e.StylusDevice.Capture(this); e.Handled = true; }
            return;
        }
        e.Handled = true;
        BeginContact(e.StylusDevice.Id, e.GetPosition(this));
        e.StylusDevice.Capture(this);
    }
    private void HandleStylusMove(object sender, StylusEventArgs e)
    {
        if (HandMode && e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus && dragPoint is Point old)
        {
            var point = e.GetPosition(this); SetView(View.Pan(point.X - old.X, point.Y - old.Y)); dragPoint = point; e.Handled = true; return;
        }
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Touch) return;
        e.Handled = true;
        MoveContact(e.StylusDevice.Id, e.GetPosition(this));
    }
    private void HandleStylusUp(object sender, StylusEventArgs e)
    {
        if (HandMode && e.StylusDevice.TabletDevice.Type == TabletDeviceType.Stylus && dragPoint is not null)
        { dragPoint = null; e.StylusDevice.Capture(null); CompleteGesture(); e.Handled = true; return; }
        if (e.StylusDevice.TabletDevice.Type != TabletDeviceType.Touch) return;
        e.Handled = true;
        EndContact(e.StylusDevice.Id);
        e.StylusDevice.Capture(null);
    }

    public void BeginContact(int id, Point position)
    {
        if (!penInRange)
        {
            if (contacts.Count == 0) { pinching = false; scrollBeyondEdge = 0; }
            contacts[id] = position;
            if (contacts.Count > 1) { pinching = true; scrollBeyondEdge = 0; }
        }
    }
    public void MoveContact(int id, Point position)
    {
        if (penInRange || !contacts.ContainsKey(id)) return;
        var before = contacts.OrderBy(p => p.Key).Take(2).Select(p => p.Value).ToArray();
        contacts[id] = position;
        var after = contacts.OrderBy(p => p.Key).Take(2).Select(p => p.Value).ToArray();
        if (before.Length == 1)
        {
            var delta = after[0].Y - before[0].Y;
            if (!pinching) TrackEdgeScroll(delta);
            SetView(View.Pan(after[0].X - before[0].X, delta));
        }
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
        if (contacts.Remove(id) && contacts.Count == 0) { GestureCompleted?.Invoke(); if (!pinching) TurnAtEdge(); }
    }
    public void SetView(PageView view)
    {
        View = view.Constrain(ActualWidth > 0 ? ActualWidth : 1000, ActualHeight > 0 ? ActualHeight : 700, PaperWidth, PaperHeight);
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
        var height = ActualHeight > 0 ? ActualHeight : 700;
        var zoom = Math.Clamp(Math.Min((width - 80) / PaperWidth, (height - 112) / PaperHeight), PageView.MinZoom, 1.5);
        SetView(new PageView(zoom, (width - PaperWidth * zoom) / 2, 88));
        GestureCompleted?.Invoke();
    }
    public void FitWidth()
    {
        var width = ActualWidth > 0 ? ActualWidth : 1000;
        var zoom = Math.Clamp((width - 80) / PaperWidth, PageView.MinZoom, 1.5);
        SetView(new PageView(zoom, (width - PaperWidth * zoom) / 2, 88));
        GestureCompleted?.Invoke();
    }
    public void CompleteGesture() => GestureCompleted?.Invoke();
    public void ConfigurePaper(double width, double height) { PaperWidth = width; PaperHeight = height; }
    private void TrackEdgeScroll(double delta)
    {
        var beyond = delta < 0 ? View.Y + PaperHeight * View.Zoom < ActualHeight - 40 : View.Y > 88;
        scrollBeyondEdge = beyond && Math.Sign(scrollBeyondEdge) == Math.Sign(delta) ? scrollBeyondEdge + delta : beyond ? delta : 0;
    }
    private void TurnAtEdge()
    {
        if (Math.Abs(scrollBeyondEdge) < 220 || penInRange) return;
        var direction = scrollBeyondEdge < 0 ? 1 : -1; scrollBeyondEdge = 0;
        PageTurnRequested?.Invoke(direction);
    }
}
