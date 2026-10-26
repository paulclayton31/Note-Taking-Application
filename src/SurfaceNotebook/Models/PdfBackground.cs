using System.Buffers.Binary;
using System.IO;

namespace SurfaceNotebook.Models;

// A self-contained page: the source retains vector/text content, and a bounded
// preview provides identical offline appearance without reparsing PDFs while writing.
public sealed record PdfBackground(byte[] SourcePdf, byte[] PreviewPng, bool RasterExport = false)
{
    public void Validate()
    {
        if (SourcePdf is null || SourcePdf.Length is < 5 or > 50_000_000 || !SourcePdf.AsSpan(0, 5).SequenceEqual("%PDF-"u8) ||
            PreviewPng is null || PreviewPng.Length is < 24 or > 16_000_000 ||
            !PreviewPng.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new InvalidDataException("Invalid PDF page background.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(PreviewPng.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(PreviewPng.AsSpan(20, 4));
        if (width is < 1 or > 4096 || height is < 1 or > 4096) throw new InvalidDataException("PDF preview dimensions are too large.");
    }
}
