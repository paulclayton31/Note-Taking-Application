using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using SurfaceNotebook.ViewModels;

namespace SurfaceNotebook.Controls;

public static class DestinationDialog
{
    public static FolderDestination? Ask(Window owner, string title, IReadOnlyList<FolderDestination> destinations, Guid? current)
    {
        var picker = new ComboBox { ItemsSource = destinations, DisplayMemberPath = "Name", MinHeight = 44, Margin = new Thickness(0, 18, 0, 20) };
        picker.SelectedItem = destinations.FirstOrDefault(d => d.Id == current) ?? destinations[0];
        var move = new Button { Content = "Move here", IsDefault = true, Style = (Style)owner.FindResource("PrimaryButton") };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Margin = new Thickness(0, 0, 8, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(cancel); buttons.Children.Add(move);
        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock { Text = title, FontSize = 20, FontWeight = FontWeights.SemiBold });
        content.Children.Add(picker); content.Children.Add(buttons);
        var window = new Window { Owner = owner, Title = title, Content = content, Width = 460, SizeToContent = SizeToContent.Height,
            WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        window.SetResourceReference(Window.BackgroundProperty, "Surface"); window.SetResourceReference(Window.ForegroundProperty, "Ink");
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 48, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0) });
        move.Click += (_, _) => window.DialogResult = true;
        return window.ShowDialog() == true ? picker.SelectedItem as FolderDestination : null;
    }
}
