using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SurfaceNotebook;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--verify-startup", StringComparer.Ordinal))
        {
            // Exercise resources, WPF layout, and rendering in the published bundle.
            // This mode exits without displaying a window or changing user files.
            try
            {
                var window = new MainWindow();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1180, 900));
                content.Arrange(new Rect(0, 0, 1180, 900));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1180, 900, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Shutdown(1);
            }
            return;
        }
        new MainWindow().Show();
    }
}
