using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Controls;

public partial class PaperSettingsPanel : UserControl
{
    private bool updating;
    private PaperSettings? draft;
    public event Action<PaperSettings, bool>? ApplyRequested;
    public event Action? CloseRequested;
    public PaperSettingsPanel() { InitializeComponent(); Load(PaperSettings.Default); }
    public void Load(PaperSettings paper)
    {
        updating = true;
        FormatPicker.SelectedIndex = (int)paper.Format;
        OrientationPicker.SelectedIndex = paper.Landscape ? 1 : 0;
        PatternPicker.SelectedIndex = (int)paper.Pattern;
        CustomWidthBox.Text = paper.CustomWidth.ToString(CultureInfo.InvariantCulture);
        CustomHeightBox.Text = paper.CustomHeight.ToString(CultureInfo.InvariantCulture);
        BackgroundBox.Text = paper.Background; LineColorBox.Text = paper.LineColor;
        SpacingSlider.Value = paper.Spacing;
        updating = false; UpdatePreview();
    }
    private void UpdatePreview()
    {
        if (updating || Preview is null || ApplyButton is null) return;
        CustomDimensions.Visibility = FormatPicker.SelectedIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        SpacingControls.Visibility = PatternPicker.SelectedIndex >= 3 ? Visibility.Visible : Visibility.Collapsed;
        SpacingLabel.Text = $"{SpacingSlider.Value:0} pt";
        try
        {
            // Hidden custom dimensions remain valid even when a standard size is chosen.
            var validWidth = double.TryParse(CustomWidthBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var width) && double.IsFinite(width);
            var validHeight = double.TryParse(CustomHeightBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var height) && double.IsFinite(height);
            if (FormatPicker.SelectedIndex == 3 && (!validWidth || !validHeight))
                throw new FormatException("Enter paper dimensions in inches, using a decimal point.");
            if (FormatPicker.SelectedIndex != 3)
            { width = validWidth ? Math.Clamp(width, 3, 36) : 8.5; height = validHeight ? Math.Clamp(height, 3, 36) : 11; }
            draft = new PaperSettings((PaperFormat)FormatPicker.SelectedIndex, OrientationPicker.SelectedIndex == 1,
                (PaperPattern)PatternPicker.SelectedIndex, width, height, SpacingSlider.Value,
                NormalizeColor(BackgroundBox.Text), NormalizeColor(LineColorBox.Text));
            draft.Validate();
            Preview.Paper = draft; Preview.Width = draft.Width; Preview.Height = draft.Height;
            ApplyButton.IsEnabled = true; ValidationMessage.Text = "";
        }
        catch (Exception ex) when (ex is FormatException or System.IO.InvalidDataException)
        {
            draft = null; ApplyButton.IsEnabled = false;
            ValidationMessage.Text = ex.Message;
        }
    }
    private static string NormalizeColor(string color) => color.Trim() is { Length: 7 } value && value[0] == '#' ? "#FF" + value[1..] : color.Trim();
    private void Setting_Changed(object sender, SelectionChangedEventArgs e) => UpdatePreview();
    private void Value_Changed(object sender, TextChangedEventArgs e) => UpdatePreview();
    private void Spacing_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePreview();
    private void BackgroundSwatch_Click(object sender, RoutedEventArgs e) => BackgroundBox.Text = (string)((Button)sender).Tag;
    private void Apply_Click(object sender, RoutedEventArgs e) { if (draft is not null) ApplyRequested?.Invoke(draft, ScopePicker.SelectedIndex == 1); }
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();
}
