using System.Windows;
using System.Windows.Controls;

namespace SurfaceNotebook.Controls;

public static class NameDialog
{
    public static string? Ask(Window owner, string title, string initial)
    {
        var text = new TextBox { Text = initial, MaxLength = 80, MinHeight = 38, Margin = new Thickness(0, 12, 0, 12) };
        var save = new Button { Content = "Save", IsDefault = true, MinWidth = 80 };
        var cancel = new Button { Content = "Cancel", IsCancel = true, MinWidth = 80 };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(save);
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(new TextBlock { Text = title, FontSize = 18 });
        panel.Children.Add(text); panel.Children.Add(buttons);
        var dialog = new Window { Owner = owner, Title = title, Width = 390, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
        save.Click += (_, _) => { if (!string.IsNullOrWhiteSpace(text.Text)) dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => { text.Focus(); text.SelectAll(); };
        return dialog.ShowDialog() == true ? text.Text.Trim() : null;
    }
}
