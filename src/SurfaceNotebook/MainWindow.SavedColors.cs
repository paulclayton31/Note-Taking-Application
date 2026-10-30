using System.Windows;
using System.Windows.Controls;
using SurfaceNotebook.Models;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly List<string> savedColors = [];
    private bool addingSavedColor;
    public IReadOnlyList<string> SavedColors => savedColors.AsReadOnly();
    public sealed record SavedColorItem(string Color, bool Selected);
    private void RefreshSavedColors()
    {
        if (SavedColorList is not null)
            SavedColorList.ItemsSource = savedColors.Select(c => new SavedColorItem(c, toolName == "Pen" && c == penColor)).ToList();
    }
    public void SelectSavedColor(string color)
    {
        if (!savedColors.Contains(color)) throw new ArgumentException("Choose a saved color.");
        var width = penWidth; var monoline = BallStyleButton.IsChecked == true;
        PenButton.IsChecked = true;
        changingTool = true;
        try { penWidth = width; ThicknessSlider.Value = width; SetPenStyle(monoline); }
        finally { changingTool = false; }
        SetInkColor(color); ApplyPen(); WritingSettingsChanged(); SavePreferences();
    }
    public void AddSavedColor(string value)
    {
        var color = NormalizeInkColor(value) ?? throw new ArgumentException("Choose a valid color.");
        if (!savedColors.Contains(color))
        {
            if (savedColors.Count >= 24) throw new InvalidOperationException("Remove a color before adding more than 24.");
            savedColors.Add(color);
        }
        SelectSavedColor(color);
    }
    public void RemoveSavedColor(string color)
    {
        savedColors.Remove(color); RefreshSavedColors(); SavePreferences();
    }
    private void SavedColor_Click(object sender, RoutedEventArgs e) => SelectSavedColor((string)((Button)sender).Tag);
    private void RemoveSavedColor_Click(object sender, RoutedEventArgs e)
    {
        if (((MenuItem)sender).Parent is ContextMenu { PlacementTarget: Button button } && button.Tag is string color)
            RemoveSavedColor(color);
    }
    private void AddSavedColor_Click(object sender, RoutedEventArgs e)
    {
        PenEditorPopup.IsOpen = false;
        ConfigurePenEditor(adding: true); addingSavedColor = true;
        ColorTitle.Text = "Add color"; NewPenOptions.Visibility = Visibility.Collapsed;
        CommitPenButton.Content = "Add color";
        PenEditorPopup.PlacementTarget = AddSavedColorButton;
        PenEditorPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        PenEditorPopup.IsOpen = true;
    }
}
