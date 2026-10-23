using System.IO;

namespace SurfaceNotebook.Models;

// These types deliberately have no dependency on WPF. Positions are page-space DIPs.
public sealed record InkPoint(double X, double Y, float Pressure);
public sealed record InkStroke(Guid Id, string Color, double Width, bool FitToCurve,
    List<InkPoint> Points);

public sealed record PageDocument(int Version, double Width, double Height,
    List<InkStroke> Strokes)
{
    public const int CurrentVersion = 1;
    public const double DefaultWidth = 816;
    public const double DefaultHeight = 1056;

    public void Validate(bool enforceSizeLimits = true)
    {
        if (Version != CurrentVersion)
            throw new InvalidDataException($"Unsupported page version: {Version}.");
        if (Width != DefaultWidth || Height != DefaultHeight)
            throw new InvalidDataException("This prototype supports a single 816 × 1056 page size.");
        if (Strokes is null || (enforceSizeLimits && Strokes.Count > 20_000))
            throw new InvalidDataException("Invalid stroke collection.");
        var ids = new HashSet<Guid>();
        long totalPoints = 0;
        foreach (var stroke in Strokes)
        {
            if (stroke is null || stroke.Id == Guid.Empty || !ids.Add(stroke.Id) ||
                stroke.Color is null || stroke.Color.Length != 9 || stroke.Color[0] != '#' ||
                !uint.TryParse(stroke.Color.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out _) ||
                !double.IsFinite(stroke.Width) || stroke.Width < 0.5 || stroke.Width > 30 ||
                stroke.Points is null || stroke.Points.Count == 0)
                throw new InvalidDataException("Invalid ink stroke.");
            totalPoints += stroke.Points.Count;
            if (enforceSizeLimits && totalPoints > 2_000_000)
                throw new InvalidDataException("Page contains too many ink points.");
            foreach (var point in stroke.Points)
                if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y) ||
                    Math.Abs(point.X) > 100_000 || Math.Abs(point.Y) > 100_000 ||
                    !float.IsFinite(point.Pressure) || point.Pressure < 0 || point.Pressure > 1)
                    throw new InvalidDataException("Invalid ink point.");
        }
    }
}
