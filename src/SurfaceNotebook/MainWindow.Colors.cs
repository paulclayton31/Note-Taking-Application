using System.Windows;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private bool creatingPen;
    private bool loadingPenEditor;
    private void InitializePenEditor()
    {
        PenColorWheel.SelectedColorChanged += color =>
        {
            if (loadingPenEditor) return;
            loadingPenEditor = true;
            try { PenBrightnessSlider.Value = PenColorWheel.Brightness; CustomColorBox.Text = color.ToString(); ColorValue.Text = ShortColor(color.ToString()); }
            finally { loadingPenEditor = false; }
            if (!creatingPen && PenEditorPopup.IsOpen) SetInkColor(color.ToString());
        };
        MainStage.SizeChanged += (_, _) => UpdateToolInspectorPlacement(ActualWidth);
    }
    private static string ShortColor(string color) => color[1..3] == "FF" ? "#" + color[3..] : color;
    private void RefreshColorChoices()
    {
        if (ColorTitle is null || creatingPen) return;
        var color = toolName == "Highlighter" ? highlighterColor : penColor;
        ColorTitle.Text = toolName == "Highlighter" ? "Highlighter color" : toolName == "Text" ? "Text color" : "Pen color";
        ColorValue.Text = ShortColor(color);
        ColorButton.ToolTip = $"{ColorTitle.Text} · {ColorValue.Text}";
        RailColorButton.ToolTip = ColorButton.ToolTip;
    }
    public bool SetInkColor(string value)
    {
        var color = NormalizeInkColor(value);
        if (color is null) return false;
        if (toolName is not ("Pen" or "Highlighter" or "Shape" or "Text")) PenButton.IsChecked = true;
        SelectInkColor(color);
        return true;
    }
    private static string? NormalizeInkColor(string value)
    {
        var color = value.Trim().ToUpperInvariant();
        if (color.Length == 7 && color[0] == '#') color = "#FF" + color[1..];
        return PaperSettings.IsColor(color) ? color : null;
    }
    private void ConfigurePenEditor(bool adding)
    {
        creatingPen = adding; loadingPenEditor = true;
        try
        {
            ColorTitle.Text = adding ? "Add pen" : toolName == "Highlighter" ? "Highlighter color" : "Ink color";
            NewPenOptions.Visibility = adding ? Visibility.Visible : Visibility.Collapsed;
            NewPenName.Text = NextPenName(); NewFountainButton.IsChecked = true;
            PenColorWheel.SelectedColor = (Color)ColorConverter.ConvertFromString(toolName == "Highlighter" ? highlighterColor : penColor);
            PenBrightnessSlider.Value = PenColorWheel.Brightness;
            CustomColorBox.Text = PenColorWheel.SelectedColor.ToString(); ColorValue.Text = ShortColor(CustomColorBox.Text);
            ColorValidation.Text = ""; CommitPenButton.Content = adding ? "Add pen" : "Done";
        }
        finally { loadingPenEditor = false; }
    }
    public void BeginAddPen()
    {
        PenEditorPopup.IsOpen = false;
        PenButton.IsChecked = true; ShowToolInspector(); ToolOptionsPopup.IsOpen = false; AllPensPopup.IsOpen = false;
        ConfigurePenEditor(adding: true);
        PenEditorPopup.PlacementTarget = AddPenButton; PenEditorPopup.IsOpen = true;
    }
    internal FrameworkElement PrepareAddPenPreview()
    { ConfigurePenEditor(adding: true); return (FrameworkElement)PenEditorPopup.Child; }
    private void AddPen_Click(object sender, RoutedEventArgs e) => BeginAddPen();
    private void ColorMenu_Click(object sender, RoutedEventArgs e)
    {
        PenEditorPopup.IsOpen = false;
        if (toolName is not ("Pen" or "Highlighter" or "Shape" or "Text")) PenButton.IsChecked = true;
        ShowToolInspector(); ToolOptionsPopup.IsOpen = false; AllPensPopup.IsOpen = false;
        ConfigurePenEditor(adding: false);
        PenEditorPopup.PlacementTarget = RailColorButton; PenEditorPopup.IsOpen = true;
    }
    private void Brightness_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    { if (!loadingPenEditor && PenColorWheel is not null) PenColorWheel.Brightness = e.NewValue; }
    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        var color = NormalizeInkColor(CustomColorBox.Text);
        if (color is null) { ColorValidation.Text = "Use a color such as #655BBD."; return; }
        PenColorWheel.SelectedColor = (Color)ColorConverter.ConvertFromString(color); ColorValidation.Text = "";
    }
    private void CommitPen_Click(object sender, RoutedEventArgs e)
    {
        if (creatingPen)
        {
            try { AddPen(NewPenName.Text, PenColorWheel.SelectedColor.ToString(), monoline: NewBallButton.IsChecked == true); }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { ColorValidation.Text = ex.Message; return; }
        }
        PenEditorPopup.IsOpen = false;
    }
    private void ClosePenEditor_Click(object sender, RoutedEventArgs e) => PenEditorPopup.IsOpen = false;
    private void PenEditor_Closed(object? sender, EventArgs e) => creatingPen = false;
    private void AllPens_Click(object sender, RoutedEventArgs e)
    { PenEditorPopup.IsOpen = false; ToolOptionsPopup.IsOpen = false; AllPensPopup.IsOpen = !AllPensPopup.IsOpen; }
    private void ToolOptions_Click(object sender, RoutedEventArgs e)
    { PenEditorPopup.IsOpen = false; AllPensPopup.IsOpen = false; ToolOptionsPopup.IsOpen = !ToolOptionsPopup.IsOpen; }
}
