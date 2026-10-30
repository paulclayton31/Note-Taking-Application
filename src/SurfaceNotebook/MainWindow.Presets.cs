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
    private const int MaxSavedPens = 24;
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
            foreach (var preset in (settings.Presets ?? PenPreset.Defaults).Where(p => p is not null && p.IsValid).DistinctBy(p => p.Id).Take(MaxSavedPens + 1)) savedPens.Add(preset);
            penColor = PaperSettings.IsColor(settings.PenColor) ? settings.PenColor : "#FF24324A";
            penWidth = double.IsFinite(settings.PenWidth) ? Math.Clamp(settings.PenWidth, .5, 12) : 2.5;
            highlighterColor = PaperSettings.IsColor(settings.HighlighterColor) ? settings.HighlighterColor : "#FFE7B944";
            highlighterWidth = double.IsFinite(settings.HighlighterWidth) ? Math.Clamp(settings.HighlighterWidth, 4, 60) : 18;
            eraserSize = double.IsFinite(settings.EraserSize) ? Math.Clamp(settings.EraserSize, 4, 60) : 24;
            SetPenStyle(settings.Monoline);
            EraserModePicker.SelectedIndex = settings.PrecisionEraser ? 1 : 0;
            ScribbleEraseToggle.IsChecked = settings.ScribbleErase;
            HoldShapesToggle.IsChecked = settings.HoldShapes;
            SelectInkColor(penColor); ThicknessSlider.Value = penWidth;
            activePreset = savedPens.FirstOrDefault(p => p.Id == settings.ActivePreset)?.Id
                ?? savedPens.FirstOrDefault(p => p.Color == penColor && Math.Abs(p.Width - penWidth) < .001 && p.Monoline == settings.Monoline)?.Id;
            if (activePreset is null)
            {
                // Preserve the detached current pen from v0.3.0 instead of losing its edits.
                var recovered = new PenPreset(Guid.NewGuid(), NextPenName(), penColor, penWidth, settings.Monoline);
                savedPens.Add(recovered); activePreset = recovered.Id;
            }
            else UpdateActivePen();
            savedColors.AddRange((settings.SavedColors ?? savedPens.Select(p => p.Color).ToList()).Where(PaperSettings.IsColor).Distinct().Take(24));
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
    private WritingPreferences CaptureWritingPreferences()
    {
        var pen = savedPens.FirstOrDefault(p => p.Id == activePreset);
        return new(savedPens.ToList(), activePreset, pen?.Color ?? penColor, pen?.Width ?? penWidth,
            pen?.Monoline ?? BallStyleButton.IsChecked == true, highlighterColor, highlighterWidth, eraserSize,
            EraserModePicker.SelectedIndex == 1, ScribbleEraseToggle.IsChecked != false, HoldShapesToggle.IsChecked != false, savedColors.ToList());
    }
    private void UpdateActivePen()
    {
        var index = savedPens.ToList().FindIndex(p => p.Id == activePreset);
        if (index >= 0) savedPens[index] = savedPens[index] with
        { Color = penColor, Width = penWidth, Monoline = BallStyleButton.IsChecked == true };
    }
    private void LoadActivePenSettings()
    {
        if (savedPens.FirstOrDefault(p => p.Id == activePreset) is not { } pen) return;
        penColor = pen.Color; penWidth = pen.Width; SetPenStyle(pen.Monoline);
    }
    private void SetPenStyle(bool monoline)
    { FountainStyleButton.IsChecked = !monoline; BallStyleButton.IsChecked = monoline; }
    private void PenStyle_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || changingTool || restoringWriting) return;
        ApplyPen(); WritingSettingsChanged();
    }
    private void WritingSettingsChanged()
    {
        if (!ready || changingTool || restoringWriting) return;
        if (toolName == "Pen") UpdateActivePen();
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
        var items = savedPens.Select(p => new PresetItem(p, toolName == "Pen" && p.Id == activePreset)).ToList();
        SavedPenList.ItemsSource = items;
        RefreshSavedColors();
        var preset = savedPens.FirstOrDefault(p => p.Id == activePreset);
        RailPensButton.ToolTip = preset is null ? "Your pens" : $"{preset.Name} · {preset.Description} · choose a pen";
        RailColorButton.ToolTip = "Choose ink color";
        AllPenList.ItemsSource = items;
        ThicknessValue.Text = ThicknessSlider.Value.ToString("0.#", CultureInfo.CurrentCulture);
    }
    private void ScribbleErase_Changed(object sender, RoutedEventArgs e) => WritingSettingsChanged();
    public PenPreset AddPen(string name, string? color = null, double? width = null, bool? monoline = null)
    {
        ValidateName(name);
        if (savedPens.Count >= MaxSavedPens) throw new InvalidOperationException($"You can have up to {MaxSavedPens} pens. Remove one before adding another.");
        if (savedPens.Any(p => p.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("Choose a different name for this saved pen.");
        var preset = new PenPreset(Guid.NewGuid(), name.Trim(), color ?? penColor, width ?? penWidth, monoline ?? BallStyleButton.IsChecked == true);
        if (!preset.IsValid) throw new ArgumentException("Choose a valid pen color and thickness between 0.5 and 12.");
        savedPens.Add(preset); ApplyPenPreset(preset.Id); return preset;
    }
    public void ApplyPenPreset(Guid id)
    {
        var preset = savedPens.First(p => p.Id == id);
        PenButton.IsChecked = true;
        changingTool = true;
        try
        {
            penColor = preset.Color; penWidth = preset.Width; activePreset = preset.Id;
            SelectInkColor(penColor); ThicknessSlider.Value = penWidth; SetPenStyle(preset.Monoline);
        }
        finally { changingTool = false; }
        ApplyPen(); RefreshSavedPens(); SavePreferences();
    }
    public void RemovePenPreset(Guid id)
    {
        var preset = savedPens.FirstOrDefault(p => p.Id == id); if (preset is null) return;
        if (savedPens.Count == 1) { model.Status = "Add another pen before removing your last pen."; return; }
        var index = savedPens.IndexOf(preset); savedPens.Remove(preset);
        if (activePreset == id) ApplyPenPreset(savedPens[Math.Min(index, savedPens.Count - 1)].Id);
        else { RefreshSavedPens(); SavePreferences(); }
    }
    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        ApplyPenPreset((Guid)((Button)sender).Tag); AllPensPopup.IsOpen = false;
    }
    private string NextPenName()
    {
        var number = 1;
        while (savedPens.Any(p => p.Name.Equals($"Pen {number}", StringComparison.OrdinalIgnoreCase))) number++;
        return $"Pen {number}";
    }
    private void PresetAction_Click(object sender, RoutedEventArgs e)
    {
        var item = (MenuItem)sender;
        if (item.Parent is not ContextMenu menu || menu.PlacementTarget is not Button button || button.Tag is not Guid id) return;
        if ((string)item.Tag == "remove") { RemovePenPreset(id); return; }
        var preset = savedPens.First(p => p.Id == id); CloseToolInspector();
        var name = NameDialog.Ask(this, "Rename saved pen", preset.Name);
        if (name is null) return;
        if (savedPens.Any(p => p.Id != id && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) { ShowError(new ArgumentException("Choose a different pen name.")); return; }
        savedPens[savedPens.IndexOf(preset)] = preset with { Name = name }; RefreshSavedPens(); SavePreferences();
    }
}
