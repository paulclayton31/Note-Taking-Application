using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly ObservableCollection<PenPreset> savedPens = [];
    private readonly DispatcherTimer writingTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private Guid? activePreset;
    private bool restoringWriting;
    private bool writingTimerInitialized;
    public IReadOnlyList<PenPreset> SavedPens => savedPens.ToList();
    private void RestoreWritingPreferences()
    {
        restoringWriting = true; changingTool = true;
        try
        {
            var settings = preferences.Writing ?? new();
            foreach (var preset in (settings.Presets ?? PenPreset.Defaults).Where(p => p is not null && p.IsValid).DistinctBy(p => p.Id).Take(12)) savedPens.Add(preset);
            penColor = PaperSettings.IsColor(settings.PenColor) ? settings.PenColor : "#FF24324A";
            penWidth = double.IsFinite(settings.PenWidth) ? Math.Clamp(settings.PenWidth, .5, 12) : 2.5;
            highlighterColor = PaperSettings.IsColor(settings.HighlighterColor) ? settings.HighlighterColor : "#FFE7B944";
            highlighterWidth = double.IsFinite(settings.HighlighterWidth) ? Math.Clamp(settings.HighlighterWidth, 4, 60) : 18;
            eraserSize = double.IsFinite(settings.EraserSize) ? Math.Clamp(settings.EraserSize, 4, 60) : 24;
            PenStylePicker.SelectedIndex = settings.Monoline ? 1 : 0;
            EraserModePicker.SelectedIndex = settings.PrecisionEraser ? 1 : 0;
            SelectInkColor(penColor); ThicknessSlider.Value = penWidth;
            activePreset = settings.ActivePreset;
        }
        finally { changingTool = false; restoringWriting = false; }
        ApplyPen(); RefreshSavedPens();
    }
    private void SelectInkColor(string color)
    {
        var match = ColorPicker.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == color);
        if (match is null)
        {
            match = new ComboBoxItem { Content = "Custom color", Tag = color };
            ColorPicker.Items.Add(match);
        }
        ColorPicker.SelectedItem = match;
    }
    private WritingPreferences CaptureWritingPreferences() => new(savedPens.ToList(), activePreset, penColor, penWidth,
        PenStylePicker?.SelectedIndex == 1, highlighterColor, highlighterWidth, eraserSize, EraserModePicker?.SelectedIndex == 1);
    private void WritingSettingsChanged()
    {
        if (!ready || changingTool || restoringWriting) return;
        if (activePreset is Guid id && savedPens.FirstOrDefault(p => p.Id == id) is { } preset &&
            (preset.Color != penColor || Math.Abs(preset.Width - penWidth) > .001 || preset.Monoline != (PenStylePicker.SelectedIndex == 1))) activePreset = null;
        RefreshSavedPens();
        if (!writingTimerInitialized)
        {
            writingTimerInitialized = true; writingTimer.Tick += (_, _) => { writingTimer.Stop(); SavePreferences(); };
        }
        writingTimer.Stop(); writingTimer.Start();
    }
    private void RefreshSavedPens()
    {
        if (SavedPenList is null) return;
        SavedPenList.ItemsSource = savedPens.Select(p => new PresetItem(p, p.Id == activePreset)).ToList();
        var preset = savedPens.FirstOrDefault(p => p.Id == activePreset);
        CurrentPenDescription.Text = $"{preset?.Name ?? "Current pen"} · {penWidth.ToString("0.#", CultureInfo.CurrentCulture)} pt · {(PenStylePicker.SelectedIndex == 1 ? "Monoline" : "Pressure")}";
        ThicknessValue.Text = ThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
    }
    public PenPreset SavePenPreset(string name)
    {
        ValidateName(name);
        if (savedPens.Count >= 12) throw new InvalidOperationException("You can save up to 12 pens. Remove one before adding another.");
        if (savedPens.Any(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a different name for this saved pen.");
        var preset = new PenPreset(Guid.NewGuid(), name.Trim(), penColor, penWidth, PenStylePicker.SelectedIndex == 1);
        savedPens.Add(preset); activePreset = preset.Id; RefreshSavedPens(); SavePreferences(); return preset;
    }
    public void ApplyPenPreset(Guid id)
    {
        var preset = savedPens.First(p => p.Id == id);
        PenButton.IsChecked = true;
        changingTool = true;
        try
        {
            penColor = preset.Color; penWidth = preset.Width; activePreset = preset.Id;
            SelectInkColor(penColor); ThicknessSlider.Value = penWidth; PenStylePicker.SelectedIndex = preset.Monoline ? 1 : 0;
        }
        finally { changingTool = false; }
        ApplyPen(); RefreshSavedPens(); SavePreferences();
    }
    public void RemovePenPreset(Guid id)
    {
        var preset = savedPens.FirstOrDefault(p => p.Id == id); if (preset is null) return;
        savedPens.Remove(preset); if (activePreset == id) activePreset = null;
        RefreshSavedPens(); SavePreferences();
    }
    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        ApplyPenPreset((Guid)((Button)sender).Tag); ToolPopup.IsOpen = false;
    }
    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        ToolPopup.IsOpen = false;
        var name = NameDialog.Ask(this, "Save pen", $"Study pen {savedPens.Count + 1}");
        if (name is not null) try { SavePenPreset(name); } catch (Exception ex) { ShowError(ex); }
    }
    private void PresetAction_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        if (item.Parent is not ContextMenu menu || menu.PlacementTarget is not Button button || button.Tag is not Guid id) return;
        if ((string)item.Tag == "remove") { RemovePenPreset(id); return; }
        var preset = savedPens.First(p => p.Id == id); ToolPopup.IsOpen = false;
        var name = NameDialog.Ask(this, "Rename saved pen", preset.Name);
        if (name is null) return;
        if (savedPens.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ShowError(new ArgumentException("Choose a different pen name.")); return; }
        savedPens[savedPens.IndexOf(preset)] = preset with { Name = name }; RefreshSavedPens(); SavePreferences();
    }
}
