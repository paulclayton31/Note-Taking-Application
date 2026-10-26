using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SurfaceNotebook.Controls;

// Pixel virtualization keeps touch panning native; only wheel steps receive easing.
public static class SmoothScroll
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached("Enabled", typeof(bool), typeof(SmoothScroll),
        new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(ScrollState), typeof(SmoothScroll));
    private static readonly DependencyProperty OffsetProperty = DependencyProperty.RegisterAttached("Offset", typeof(double), typeof(SmoothScroll),
        new PropertyMetadata(0d, (d, e) => ((ScrollViewer)d).ScrollToVerticalOffset((double)e.NewValue)));
    public static void SetEnabled(DependencyObject element, bool value) => element.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject element) => (bool)element.GetValue(EnabledProperty);
    private static void EnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        if ((bool)e.NewValue) element.SetValue(StateProperty, new ScrollState(element));
        else if (element.GetValue(StateProperty) is ScrollState state) { state.Detach(); element.ClearValue(StateProperty); }
    }
    private sealed class ScrollState
    {
        private readonly FrameworkElement owner;
        private ScrollViewer? viewer;
        private double target;
        private bool animating;
        public ScrollState(FrameworkElement owner)
        {
            this.owner = owner;
            owner.PreviewMouseWheel += Wheel; owner.PreviewMouseDown += CancelMouse;
            owner.PreviewTouchDown += CancelTouch; owner.PreviewKeyDown += CancelKey; owner.Unloaded += Unloaded; owner.RequestBringIntoView += BringIntoView; owner.SizeChanged += SizeChanged;
        }
        public void Detach()
        {
            Stop(); owner.PreviewMouseWheel -= Wheel; owner.PreviewMouseDown -= CancelMouse;
            owner.PreviewTouchDown -= CancelTouch; owner.PreviewKeyDown -= CancelKey; owner.Unloaded -= Unloaded; owner.RequestBringIntoView -= BringIntoView; owner.SizeChanged -= SizeChanged;
        }
        private void BringIntoView(object sender, RequestBringIntoViewEventArgs e) => Stop();
        private void SizeChanged(object sender, SizeChangedEventArgs e) => Stop();
        private void CancelMouse(object sender, MouseButtonEventArgs e) => Stop();
        private void CancelTouch(object? sender, TouchEventArgs e) => Stop();
        private void CancelKey(object sender, KeyEventArgs e) => Stop();
        private void Unloaded(object sender, RoutedEventArgs e) => Stop();
        private void Stop()
        {
            if (viewer is null || !animating) return;
            var current = viewer.VerticalOffset; animating = false;
            viewer.BeginAnimation(OffsetProperty, null); viewer.SetValue(OffsetProperty, current);
        }
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            viewer ??= FindViewer(owner);
            if (viewer is null || viewer.ScrollableHeight <= 0 || SystemParameters.WheelScrollLines == 0) return;
            var distance = SystemParameters.WheelScrollLines < 0 ? viewer.ViewportHeight : SystemParameters.WheelScrollLines * 16;
            var from = viewer.VerticalOffset;
            var next = Math.Clamp((animating ? target : from) - e.Delta / 120d * distance, 0, viewer.ScrollableHeight);
            if (Math.Abs(next - from) < .1 && !animating) return;
            Stop(); target = next; e.Handled = true;
            if (!SystemParameters.ClientAreaAnimation) { viewer.ScrollToVerticalOffset(next); return; }
            viewer.SetValue(OffsetProperty, from); animating = true;
            var animation = new DoubleAnimation(from, next, TimeSpan.FromMilliseconds(140))
            { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) => { if (animating) { animating = false; viewer.SetValue(OffsetProperty, target); } };
            viewer.BeginAnimation(OffsetProperty, animation, HandoffBehavior.SnapshotAndReplace);
        }
        private static ScrollViewer? FindViewer(DependencyObject parent)
        {
            if (parent is ScrollViewer viewer) return viewer;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                if (FindViewer(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
            return null;
        }
    }
}
