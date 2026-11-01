using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckImagePaste(string root)
    {
        var previous = Clipboard.GetDataObject();
        var window = new MainWindow(root);
        Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        var ink = (PenCanvas)window.FindName("InkSurface");
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null,
            new byte[] {0, 0, 255, 255, 0, 255, 0, 255, 255, 0, 0, 255, 0, 0, 0, 0}, 8);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        var png = stream.ToArray();
        var page = window.CurrentPageId!.Value;
        try
        {
            // Browser-style PNG clipboard data, including a transparent pixel.
            var data = new DataObject(); data.SetData("PNG", new MemoryStream(png)); Clipboard.SetDataObject(data, true);
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            window.PasteSelectedObjects(new Point(100, 120));
            var image = ink.Children.OfType<Image>().Single(); var identity = (Guid)image.Tag;
            Require(InkCanvas.GetLeft(image) == 100 && InkCanvas.GetTop(image) == 120 && ink.GetSelectedElements().Contains(image),
                "Pasted browser image was not positioned or selected.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Children.Count == 0, "Image paste did not undo in one edit.");
            Click((Button)window.FindName("RedoButton")); Require(ink.Children.OfType<Image>().Count() == 1, "Image paste did not redo.");
            // Copy/duplicate/delete must include images as well as ink and text.
            ink.Select(new StrokeCollection(), ink.Children.Cast<UIElement>());
            window.DuplicateSelectedObjects(); Require(ink.Children.OfType<Image>().Count() == 2, "Image duplicate lost content.");
            window.DeleteSelectedObjects(); Require(ink.Children.OfType<Image>().Count() == 1, "Deleting the duplicate changed the source.");
            ink.Select(new StrokeCollection(), ink.Children.Cast<UIElement>()); window.CopySelectedObjects();
            var second = Await(window.CreatePageAsync("Image copy"));
            window.PasteSelectedObjects(); Require(ink.Children.OfType<Image>().Count() == 1, "Image copy/paste across pages failed.");
            Require((Guid)ink.Children.OfType<Image>().Single().Tag != identity, "Image copy reused its identity.");
            // Standard bitmap clipboard fallback (e.g. screenshots).
            Clipboard.SetImage(bitmap); ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            window.PasteSelectedObjects(); Require(ink.Children.OfType<Image>().Count() == 2, "Bitmap clipboard fallback failed in Pen mode.");
            Click((Button)window.FindName("UndoButton"));
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            var menu = ink.ContextMenu!; menu.Opacity = 0; menu.PlacementTarget = ink; menu.IsOpen = true;
            Require(menu.Items.OfType<MenuItem>().Single(i => (string)i.Header == "Paste").IsEnabled, "Right-click Paste rejected a bitmap outside Lasso mode.");
            menu.IsOpen = false;
            Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "Pasted images did not save.");
            using (var db = new NotebookDatabase(root))
            {
                var saved = db.LoadPage(page); Require(saved.Images!.Single().Png.SequenceEqual(png), "Saving changed the image pixels.");
                var duplicate = db.DuplicatePage(page, Guid.NewGuid(), "Image duplicate");
                Require(duplicate.Document.Images!.Single().Id != saved.Images!.Single().Id, "Page duplication reused an image identity.");
                var portable = Path.Combine(root, "image.stempage"); Await(PageStore.SaveAsync(portable, saved));
                Require(Await(PageStore.LoadAsync(portable)).Images!.Single().Png.SequenceEqual(png), "Portable export lost the image.");
                Await(PdfService.ExportAsync(Path.Combine(root, "image.pdf"), [saved], "Image export"));
                db.MarkClean();
            }
            var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
            Await(reopened.ActivatePageAsync(second));
            Require(((PenCanvas)reopened.FindName("InkSurface")).Children.OfType<Image>().Count() == 1, "Restart lost the saved image.");
            Require(Await(reopened.ShutdownLibraryAsync()), "Image test did not close cleanly.");
            // Image metadata is bounded before decoding external/portable content.
            var invalid = new PageImage(Guid.NewGuid(), 0, 0, double.NaN, 1, png);
            try { invalid.Validate(); throw new Exception("Invalid image dimensions accepted."); } catch (InvalidDataException) { }
        }
        finally
        {
            Await(window.ShutdownLibraryAsync());
            if (previous is null) Clipboard.Clear(); else Clipboard.SetDataObject(previous, true);
        }
    }
}
