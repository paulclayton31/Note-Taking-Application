using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private string? toolBeforeClick;
    public bool IsToolInspectorOpen => ContextPanel.Visibility == Visibility.Visible && ToolInspector.Visibility == Visibility.Visible;
    private void Tool_PointerDown(object sender, MouseButtonEventArgs e) => toolBeforeClick = toolName;
    private void Tool_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key is Key.Space or Key.Enter) toolBeforeClick = toolName; }
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && (string)button.Tag == toolBeforeClick) ToggleToolInspector();
        toolBeforeClick = null;
    }
    private void ToggleToolInspector()
    {
        if (IsToolInspectorOpen) CloseToolInspector(); else ShowToolInspector();
    }
    public void ShowToolInspector()
    {
        if (!ready || busy || homeVisible) return;
        CommitTextEditor(); ZoomPopup.IsOpen = false; selectionMenu.IsOpen = false;
        PaperSettingsEditor.Visibility = Visibility.Collapsed; ToolInspector.Visibility = Visibility.Visible;
        ContextPanel.Width = 252; ContextPanel.Visibility = Visibility.Visible;
        UpdateToolInspectorPlacement(ActualWidth > 0 ? ActualWidth : ((FrameworkElement)Content).ActualWidth);
        CustomColorBox.Text = toolName == "Highlighter" ? highlighterColor : penColor;
        ColorValidation.Text = ""; RefreshColorChoices();
        PropertiesButton.SetResourceReference(Control.BackgroundProperty, "Selected");
        AnimatePanel(ContextPanel);
    }
    public void CloseToolInspector()
    {
        if (!IsToolInspectorOpen) return;
        ContextPanel.Visibility = Visibility.Collapsed;
        PropertiesButton.Background = System.Windows.Media.Brushes.Transparent;
    }
    private void CloseToolInspector_Click(object sender, RoutedEventArgs e) => CloseToolInspector();
    private void UpdateToolInspectorPlacement(double width)
    {
        if (!IsToolInspectorOpen) return;
        // Overlay in portrait/compact windows so opening settings never narrows the page.
        Grid.SetColumn(ContextPanel, width < 1280 ? 1 : 2);
        ContextPanel.HorizontalAlignment = HorizontalAlignment.Right;
        Panel.SetZIndex(ContextPanel, 20);
    }
}
