using System.Windows;
using SurfaceNotebook.Models;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private static readonly (string Hex, string Name)[] InkColors =
    [
        ("#FF24324A", "Midnight"), ("#FF000000", "Black"), ("#FF697386", "Slate"),
        ("#FF5C55B3", "Indigo"), ("#FF4D8EC9", "Sky"), ("#FF244DB5", "Blue"),
        ("#FFCE3F53", "Rose"), ("#FFE77591", "Pink"), ("#FF9A4AB5", "Violet"),
        ("#FF248779", "Teal"), ("#FF387D42", "Green"), ("#FF89A94B", "Olive"),
        ("#FFE7B944", "Amber"), ("#FFF1DC55", "Yellow"), ("#FFE57B38", "Orange"),
        ("#FF87573B", "Brown"), ("#FFBBC6D6", "Silver"), ("#FFFFFFFF", "White")
    ];

    private void RefreshColorChoices()
    {
        if (ColorSwatches is null) return;
        var color = toolName == "Highlighter" ? highlighterColor : penColor;
        ColorSwatches.ItemsSource = InkColors.Select(c => new InkColorChoice(c.Hex, c.Name, c.Hex == color)).ToList();
        ColorTitle.Text = toolName == "Highlighter" ? "Highlighter color" : toolName == "Text" ? "Text color" : "Pen color";
        ColorValue.Text = color[1..3] == "FF" ? "#" + color[3..] : color;
        ColorButton.ToolTip = $"{ColorTitle.Text} · {ColorValue.Text}";
    }
    public bool SetInkColor(string value)
    {
        var color = value.Trim().ToUpperInvariant();
        if (color.Length == 7 && color[0] == '#') color = "#FF" + color[1..];
        if (!PaperSettings.IsColor(color)) return false;
        if (toolName is not ("Pen" or "Highlighter" or "Shape" or "Text")) PenButton.IsChecked = true;
        SelectInkColor(color);
        return true;
    }
    private void ColorMenu_Click(object sender, RoutedEventArgs e)
    {
        if (toolName is not ("Pen" or "Highlighter" or "Shape" or "Text")) PenButton.IsChecked = true;
        selectionMenu.IsOpen = false;
        RefreshColorChoices();
        CustomColorBox.Text = toolName == "Highlighter" ? highlighterColor : penColor;
        ColorValidation.Text = "";
        ShowToolInspector();
        Dispatcher.BeginInvoke(() => ColorControls.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
    }
    private void CustomColor_Click(object sender, RoutedEventArgs e)
    {
        if (SetInkColor(CustomColorBox.Text)) ColorValidation.Text = "";
        else ColorValidation.Text = "Use a hex color such as #655BBD or #FF655BBD.";
    }
}
