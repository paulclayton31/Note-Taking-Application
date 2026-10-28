using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

public static class PdfService
{
    public static Task<List<PageDocument>> ImportAsync(string path, CancellationToken cancellation = default, IProgress<string>? progress = null) => Task.Run(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (new FileInfo(path).Length > 100_000_000) throw new InvalidDataException("Choose a PDF smaller than 100 MB.");
        var bytes = File.ReadAllBytes(path); cancellation.ThrowIfCancellationRequested();
        using var input = new MemoryStream(bytes, writable: false);
        using var original = PdfReader.Open(input, PdfDocumentOpenMode.Import);
        if (original.SecuritySettings.IsEncrypted) throw new InvalidDataException("Save an unlocked copy of this PDF before importing it.");
        if (original.PageCount is < 1 or > 200) throw new InvalidDataException("Import PDFs with 1–200 pages at a time.");
        foreach (var page in original.Pages)
            if (Enumerable.Range(0, page.Annotations.Count).Any(index => page.Annotations[index].Elements.GetName("/Subtype") == "/Widget"))
                throw new InvalidDataException("This PDF contains form or signature fields. Print or flatten it to a regular PDF before importing.");
        lock (PdfiumDocument.Gate)
        {
            using var native = new PdfiumDocument(bytes);
            if (native.PageCount != original.PageCount) throw new InvalidDataException("The PDF page count could not be verified.");
            var result = new List<PageDocument>(); long totalBytes = 0;
            for (var i = 0; i < original.PageCount; i++)
            {
                cancellation.ThrowIfCancellationRequested(); progress?.Report($"Preparing PDF page {i + 1} of {original.PageCount}…");
                var rendered = native.Render(i, cancellation);
                var paper = new PaperSettings(Format: PaperFormat.Custom, Pattern: PaperPattern.Blank,
                    CustomWidth: rendered.Width / 96, CustomHeight: rendered.Height / 96, Background: "#FFFFFFFF");
                paper.Validate();
                using var single = new PdfDocument(); var source = original.Pages[i]; single.AddPage(source);
                using var pdf = new MemoryStream(); single.Save(pdf, closeStream: false);
                // Complex source pages use their exact rendered appearance on export.
                var raster = source.Rotate != 0 || (source.Elements.ContainsKey("/CropBox") && source.CropBox != source.MediaBox) || source.Annotations.Count > 0;
                var background = new PdfBackground(pdf.ToArray(), rendered.Png, raster); background.Validate();
                totalBytes += background.SourcePdf.Length + background.PreviewPng.Length;
                if (totalBytes > 300_000_000) throw new InvalidDataException("This PDF is too large to import in one notebook. Split it into smaller PDFs.");
                var document = new PageDocument(PageDocument.CurrentVersion, paper.Width, paper.Height, [], paper, [], background);
                document.Validate(); result.Add(document);
            }
            return result;
        }
    }, cancellation);

    public static BitmapImage LoadPreview(PdfBackground background, int decodeWidth = 0)
    {
        background.Validate();
        using var stream = new MemoryStream(background.PreviewPng, writable: false);
        var image = new BitmapImage(); image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) image.DecodePixelWidth = decodeWidth;
        image.StreamSource = stream; image.EndInit(); image.Freeze(); return image;
    }

    public static Task ExportAsync(string path, IReadOnlyList<PageDocument> pages, string title, CancellationToken cancellation = default, IProgress<string>? progress = null)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { Export(path, pages, title, cancellation, progress); completion.SetResult(); }
            catch (OperationCanceledException) { completion.SetCanceled(cancellation); }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "PDF export" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); return completion.Task;
    }
    private static void Export(string path, IReadOnlyList<PageDocument> pages, string title, CancellationToken cancellation, IProgress<string>? progress)
    {
        if (pages.Count == 0) throw new InvalidOperationException("There are no pages to export.");
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var pdf = new PdfDocument(); pdf.Info.Title = title; pdf.Info.Creator = "Surface Notebook";
            for (var i = 0; i < pages.Count; i++)
            {
                cancellation.ThrowIfCancellationRequested(); progress?.Report($"Exporting page {i + 1} of {pages.Count}…");
                var document = pages[i]; document.Validate(enforceSizeLimits: false);
                var page = pdf.AddPage(); page.Width = XUnit.FromPoint(document.Width * .75); page.Height = XUnit.FromPoint(document.Height * .75);
                using var gfx = XGraphics.FromPdfPage(page, XGraphicsUnit.Presentation);
                gfx.IntersectClip(new XRect(0, 0, document.Width, document.Height));
                if (document.Background is { } background)
                {
                    using var stream = new MemoryStream(background.RasterExport ? background.PreviewPng : background.SourcePdf, writable: false);
                    using var image = background.RasterExport ? XImage.FromStream(stream) : XPdfForm.FromStream(stream);
                    gfx.DrawImage(image, 0, 0, document.Width, document.Height);
                }
                else DrawPaper(gfx, document.Paper ?? PaperSettings.Default with { Format = PaperFormat.Custom, CustomWidth = document.Width / 96, CustomHeight = document.Height / 96 });
                foreach (var stroke in InkMapper.Restore(document))
                {
                    var color = stroke.DrawingAttributes.Color;
                    var alpha = stroke.DrawingAttributes.IsHighlighter ? (byte)(color.A * .5) : color.A;
                    DrawGeometry(gfx, stroke.GetGeometry(), XColor.FromArgb(alpha, color.R, color.G, color.B));
                }
                foreach (var text in document.Texts ?? [])
                {
                    var inset = text.CalculationSource is null ? 2 : 9;
                    var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Variable, Segoe UI"), text.FontSize, Brushes.Black, 1) { MaxTextWidth = Math.Max(1, text.Width - inset * 2) };
                    if (text.CalculationSource is not null)
                        gfx.DrawRectangle(new XPen(XColor.FromArgb(96, 92, 85, 179), 1), new XSolidBrush(XColor.FromArgb(16, 92, 85, 179)),
                            text.X, text.Y, text.Width, formatted.Height + inset * 2);
                    var color = (Color)ColorConverter.ConvertFromString(text.Color);
                    DrawGeometry(gfx, formatted.BuildGeometry(new Point(text.X + inset, text.Y + inset)), XColor.FromArgb(color.A, color.R, color.G, color.B));
                }
            }
            cancellation.ThrowIfCancellationRequested();
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            { pdf.Save(stream, closeStream: false); stream.Flush(flushToDisk: true); }
            using (var verify = PdfReader.Open(temporary, PdfDocumentOpenMode.Import))
                if (verify.PageCount != pages.Count) throw new InvalidDataException("The exported PDF could not be verified.");
            cancellation.ThrowIfCancellationRequested(); File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private static void DrawGeometry(XGraphics gfx, Geometry geometry, XColor color)
    {
        var flattened = geometry.GetFlattenedPathGeometry(.12, ToleranceType.Absolute);
        var path = new XGraphicsPath { FillMode = flattened.FillRule == FillRule.EvenOdd ? XFillMode.Alternate : XFillMode.Winding };
        var transform = flattened.Transform?.Value ?? Matrix.Identity;
        var any = false;
        foreach (var figure in flattened.Figures)
        {
            var points = new List<XPoint> { new(figure.StartPoint.X, figure.StartPoint.Y) };
            foreach (var segment in figure.Segments)
            {
                if (segment is PolyLineSegment poly) points.AddRange(poly.Points.Select(p => new XPoint(p.X, p.Y)));
                else if (segment is LineSegment line) points.Add(new XPoint(line.Point.X, line.Point.Y));
            }
            if (points.Count < 2) continue;
            any = true;
            var transformed = points.Select(p => transform.Transform(new Point(p.X, p.Y))).Select(p => new XPoint(p.X, p.Y)).ToArray();
            path.StartFigure(); path.AddLines(transformed); if (figure.IsClosed) path.CloseFigure();
        }
        if (any) gfx.DrawPath(new XSolidBrush(color), path);
    }
    private static void DrawPaper(XGraphics gfx, PaperSettings paper)
    {
        static XColor ColorOf(string hex) { var c = (Color)ColorConverter.ConvertFromString(hex); return XColor.FromArgb(c.A, c.R, c.G, c.B); }
        gfx.DrawRectangle(new XSolidBrush(ColorOf(paper.Background)), 0, 0, paper.Width, paper.Height);
        var pen = new XPen(ColorOf(paper.LineColor), .7); var spacing = paper.LineSpacing;
        if (paper.Pattern is PaperPattern.College or PaperPattern.Wide)
            for (var y = 80d; y < paper.Height - 24; y += spacing) gfx.DrawLine(pen, 32, y, paper.Width - 32, y);
        else if (paper.Pattern is PaperPattern.Graph or PaperPattern.Engineering)
        {
            for (var x = spacing; x < paper.Width; x += spacing) gfx.DrawLine(pen, x, 0, x, paper.Height);
            for (var y = spacing; y < paper.Height; y += spacing) gfx.DrawLine(pen, 0, y, paper.Width, y);
            if (paper.Pattern == PaperPattern.Engineering)
            {
                pen.Width = 1.5;
                for (var x = spacing * 5; x < paper.Width; x += spacing * 5) gfx.DrawLine(pen, x, 0, x, paper.Height);
                for (var y = spacing * 5; y < paper.Height; y += spacing * 5) gfx.DrawLine(pen, 0, y, paper.Width, y);
            }
        }
        else if (paper.Pattern == PaperPattern.Dots)
            for (var x = spacing; x < paper.Width; x += spacing)
                for (var y = spacing; y < paper.Height; y += spacing) gfx.DrawEllipse(new XSolidBrush(pen.Color), x - 1, y - 1, 2, 2);
    }
}
