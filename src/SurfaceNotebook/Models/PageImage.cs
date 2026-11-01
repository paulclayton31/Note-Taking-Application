using System.Buffers.Binary;
using System.IO;

namespace SurfaceNotebook.Models;

public sealed record PageImage(Guid Id, double X, double Y, double Width, double Height, byte[] Png)
{
    public void Validate()
    {
        if (Id == Guid.Empty || !double.IsFinite(X) || !double.IsFinite(Y) || Math.Abs(X) > 100000 || Math.Abs(Y) > 100000 ||
            !double.IsFinite(Width) || !double.IsFinite(Height) || Width < 1 || Height < 1 || Width > 3456 || Height > 3456 ||
            Png is null || Png.Length is < 24 or > 16_000_000 || !Png.AsSpan(0, 8).SequenceEqual(new byte[] {137, 80, 78, 71, 13, 10, 26, 10}))
            throw new InvalidDataException("Invalid page image.");
        var w = BinaryPrimitives.ReadUInt32BigEndian(Png.AsSpan(16, 4));
        var h = BinaryPrimitives.ReadUInt32BigEndian(Png.AsSpan(20, 4));
        if (w is < 1 or > 8192 || h is < 1 or > 8192 || (long)w * h > 32_000_000)
            throw new InvalidDataException("Image dimensions are too large.");
    }
}
