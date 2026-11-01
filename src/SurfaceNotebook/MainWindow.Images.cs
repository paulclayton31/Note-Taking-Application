using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private static bool HasClipboardImage()
    {
        try { return Clipboard.ContainsImage() || Clipboard.ContainsData("PNG"); }
        catch (ExternalException) { return false; }
    }
    private static bool CanPastePageContent() => Clipboard.ContainsData(SelectionClipboardFormat) || HasClipboardImage();

    private void PasteClipboardImage(Point? position)
    {
        if (session is null) return;
        BitmapSource? bitmap = null;
        // Browsers commonly supply both PNG and a device-independent bitmap.
        // Prefer PNG to retain transparency.
        if (Clipboard.GetData("PNG") is MemoryStream png)
        {
            if (png.Length > 16_000_000) throw new InvalidDataException("This image is too large to paste.");
            bitmap = PageImageSource.Decode(png.ToArray());
        }
        else if (Clipboard.GetData("PNG") is byte[] bytes)
        {
            if (bytes.Length > 16_000_000) throw new InvalidDataException("This image is too large to paste.");
            bitmap = PageImageSource.Decode(bytes);
        }
        bitmap ??= Clipboard.GetImage();
        if (bitmap is null)
        { model.Status = "Copy an image using your browser's Copy image command, then paste here."; return; }
        if (bitmap.PixelWidth is < 1 or > 8192 || bitmap.PixelHeight is < 1 or > 8192 || (long)bitmap.PixelWidth * bitmap.PixelHeight > 32_000_000)
            throw new InvalidDataException("This image is too large to paste.");
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var encoded = new MemoryStream(); encoder.Save(encoded);
        var scale = Math.Min(1, Math.Min(session.Paper.Width * .8 / bitmap.PixelWidth, session.Paper.Height * .8 / bitmap.PixelHeight));
        var image = new PageImage(Guid.NewGuid(), 0, 0, bitmap.PixelWidth * scale, bitmap.PixelHeight * scale, encoded.ToArray());
        image.Validate();
        var center = Viewport.TranslatePoint(new Point(Viewport.ActualWidth / 2, Viewport.ActualHeight / 2), InkSurface);
        var target = position ?? new Point(center.X - image.Width / 2, center.Y - image.Height / 2);
        InsertSelection(new PageDocument(PageDocument.CurrentVersion, session.Paper.Width, session.Paper.Height, [], session.Paper, Images: [image]), target);
        model.Status = "Image pasted · drag or resize the selection";
    }

    private static Image CreateImage(PageImage image)
    {
        var element = new Image { Tag = image.Id, Source = PageImageSource.Decode(image.Png), Width = image.Width, Height = image.Height,
            Stretch = Stretch.Fill, Opacity = 0, IsHitTestVisible = false };
        InkCanvas.SetLeft(element, image.X); InkCanvas.SetTop(element, image.Y);
        return element;
    }
}
