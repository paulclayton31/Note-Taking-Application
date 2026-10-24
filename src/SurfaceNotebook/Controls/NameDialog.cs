using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shell;

namespace SurfaceNotebook.Controls;

public static class NameDialog
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var text = new TextBox { Text = initial, MaxLength = 80, MinHeight = 38, Margin = new Thickness(0, 12, 0, 12) };
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        save.Style = (Style)owner.FindResource("PrimaryButton");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        System.Windows.Automation.AutomationProperties.SetName(text, title);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        panel.Children.Add(text); panel.Children.Add(buttons);
        var dialog = new Window { Owner = owner, Title = title, Width = 420, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel, FontFamily = new FontFamily("Segoe UI Variable, Segoe UI") };
        dialog.SetResourceReference(Window.BackgroundProperty, "Surface");
        dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(dialog, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return dialog.ShowDialog() == true ? text.Text.Trim() : null;
    }
}
