using System.Windows;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var captureIndex = Array.IndexOf(e.Args, "--capture-preview");
        if (captureIndex >= 0)
        {
            try
            {
                if (captureIndex + 1 >= e.Args.Length) throw new ArgumentException("Provide an output directory for preview screenshots.");
                await PreviewCapture.GenerateAsync(Path.GetFullPath(e.Args[captureIndex + 1]));
                Shutdown(0);
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--verify-startup", StringComparer.Ordinal))
        {
            // Exercise resources, WPF layout, and rendering in the published bundle.
            // This mode exits without displaying a window or changing user files.
            var directory = Path.Combine(Path.GetTempPath(), "SurfaceNotebook-startup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var window = new MainWindow(directory);
                await window.InitializeLibraryAsync();
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(1180, 900));
                content.Arrange(new Rect(0, 0, 1180, 900));
                content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1180, 900, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(content);
                var pdfPath = Path.Combine(directory, "startup-check.pdf");
                var paper = Models.PaperSettings.Default;
                await PdfService.ExportAsync(pdfPath, [new Models.PageDocument(Models.PageDocument.CurrentVersion, paper.Width, paper.Height, [], paper)], "Startup check");
                if ((await PdfService.ImportAsync(pdfPath)).Count != 1) throw new IOException("Bundled PDF rendering verification failed.");
                if (!await window.ShutdownLibraryAsync()) throw new IOException("Notebook startup verification could not flush storage.");
                Shutdown(0);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                Shutdown(1);
            }
            finally { try { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); } catch (IOException) { } }
            return;
        }
        new MainWindow().Show();
    }
}
