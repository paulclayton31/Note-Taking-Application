using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public static class CalculationCapture
{
    public static byte[] Create(PageDocument page, Rect region)
    {
        region.Intersect(new Rect(0, 0, page.Width, page.Height));
        if (region.IsEmpty || region.Width < 1 || region.Height < 1) throw new InvalidOperationException("Select a problem on the page first.");
        var scale = Math.Min(2, 2048 / Math.Max(region.Width, region.Height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(scale, scale));
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, region.Width, region.Height)));
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, region.Width, region.Height));
            dc.PushTransform(new TranslateTransform(-region.X, -region.Y));
            if (page.Background is { } background) dc.DrawImage(PdfService.LoadPreview(background, 2048), new Rect(0, 0, page.Width, page.Height));
            else dc.DrawRectangle(PaperSurface.Brush((page.Paper ?? PaperSettings.Default).Background), null, new Rect(0, 0, page.Width, page.Height));
            foreach (var image in page.Images ?? []) dc.DrawImage(PageImageSource.Decode(image.Png), new Rect(image.X, image.Y, image.Width, image.Height));
            InkMapper.Restore(page).Draw(dc);
            foreach (var text in page.Texts ?? [])
            {
                var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Segoe UI Variable, Segoe UI"), text.FontSize, PaperSurface.Brush(text.Color), 1)
                    { MaxTextWidth = Math.Max(1, text.Width - 4) };
                dc.DrawText(formatted, new Point(text.X + 2, text.Y + 2));
            }
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(region.Width * scale)),
            Math.Max(1, (int)Math.Ceiling(region.Height * scale)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream(); encoder.Save(stream);
        if (stream.Length > 8_000_000) throw new InvalidOperationException("Select a smaller problem; this image is too large to upload.");
        return stream.ToArray();
    }
}
