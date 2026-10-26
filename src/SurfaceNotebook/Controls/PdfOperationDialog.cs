using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;

namespace SurfaceNotebook.Controls;

public static class PdfOperationDialog
{
    public static T Run<T>(Window owner, string title, Func<CancellationToken, IProgress<string>, Task<T>> operation)
    {
        using var cancellation = new CancellationTokenSource();
        var status = new TextBlock { Text = "Preparing…", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) };
        var cancel = new Button { Content = "Cancel", IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, MinWidth = 88, Margin = new Thickness(0, 18, 0, 0) };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(status); panel.Children.Add(new ProgressBar { IsIndeterminate = true, Height = 4 }); panel.Children.Add(cancel);
        var dialog = new Window { Owner = owner, Title = title, Width = 390, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false, Content = panel };
        dialog.SetResourceReference(Window.BackgroundProperty, "Surface"); dialog.SetResourceReference(Window.ForegroundProperty, "Ink");
        var done = false; T? result = default; Exception? error = null;
        void Cancel() { cancellation.Cancel(); cancel.IsEnabled = false; status.Text = "Canceling…"; }
        cancel.Click += (_, _) => Cancel();
        dialog.Closing += (_, e) => { if (!done) { e.Cancel = true; Cancel(); } };
        dialog.Loaded += async (_, _) =>
        {
            try { result = await operation(cancellation.Token, new Progress<string>(value => { if (!cancellation.IsCancellationRequested) status.Text = value; })); }
            catch (Exception ex) { error = ex; }
            finally { done = true; dialog.Close(); }
        };
        dialog.ShowDialog();
        if (error is not null) ExceptionDispatchInfo.Capture(error).Throw();
        return result!;
    }
}
