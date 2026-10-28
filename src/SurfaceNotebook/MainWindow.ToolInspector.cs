using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private string? toolBeforeClick;
    public bool IsToolInspectorOpen => ToolInspector.Visibility == Visibility.Visible;
    private void Tool_PointerDown(object sender, MouseButtonEventArgs e) => toolBeforeClick = toolName;
    private void Tool_PreviewKeyDown(object sender, KeyEventArgs e)
    { if (e.Key is Key.Space or Key.Enter) toolBeforeClick = toolName; }
    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        SetBarrelSelection(false);
        if (sender is RadioButton button && (string)button.Tag == toolBeforeClick) ToggleToolInspector();
        toolBeforeClick = null;
    }
    private void ToggleToolInspector()
    {
        if (CalculationMode) { OpenCalculationEditor(useSelection: true); return; }
        if (IsToolInspectorOpen) CloseToolInspector(); else ShowToolInspector();
    }
    public void ShowToolInspector()
    {
        if (!ready || busy || homeVisible) return;
        SetBarrelSelection(false);
        CommitTextEditor(); ZoomPopup.IsOpen = false; selectionMenu.IsOpen = false;
        CloseContextPanel(); ToolInspector.Visibility = Visibility.Visible;
        UpdateToolInspectorPlacement(ActualWidth);
        RefreshColorChoices();
        PropertiesButton.SetResourceReference(Control.BackgroundProperty, "Selected");
        AnimatePanel(ToolInspector);
    }
    public void CloseToolInspector()
    {
        PenEditorPopup.IsOpen = false; AllPensPopup.IsOpen = false; ToolOptionsPopup.IsOpen = false;
        creatingPen = false;
        if (!IsToolInspectorOpen) return;
        ToolInspector.Visibility = Visibility.Collapsed;
        PropertiesButton.Background = System.Windows.Media.Brushes.Transparent;
    }
    private void CloseToolInspector_Click(object sender, RoutedEventArgs e) => CloseToolInspector();
    private void UpdateToolInspectorPlacement(double width)
    {
        if (!IsToolInspectorOpen) return;
        var available = Math.Max(44, MainStage.ActualHeight - 24);
        var compactRail = available < 480;
        ToolInspector.MaxHeight = available;
        RailPensButton.Visibility = compactRail ? Visibility.Visible : Visibility.Collapsed;
        SavedPenScroll.Visibility = compactRail ? Visibility.Collapsed : Visibility.Visible;
        ThicknessSlider.Height = Math.Clamp(available - (compactRail ? 294 : 382), 96, 200);
    }
}
