using System.IO;

namespace SurfaceNotebook.Models;

// These types deliberately have no dependency on WPF. Positions are page-space DIPs.
public sealed record InkPoint(double X, double Y, float Pressure);
public sealed record InkStroke(Guid Id, string Color, double Width, bool FitToCurve,
    List<InkPoint> Points, double? Height = null, bool IsHighlighter = false, bool IgnorePressure = false, bool RectangleTip = false);

public sealed record PageDocument(int Version, double Width, double Height,
    List<InkStroke> Strokes, PaperSettings? Paper = null, List<PageText>? Texts = null, PdfBackground? Background = null)
{
    public const int CurrentVersion = 3;
    public const double DefaultWidth = 816;
    public const double DefaultHeight = 1056;

    public void Validate(bool enforceSizeLimits = true)
    {
        if (Version is not 1 and not 2 and not CurrentVersion)
            throw new InvalidDataException($"Unsupported page version: {Version}.");
        if (!double.IsFinite(Width) || !double.IsFinite(Height) || Width < 288 || Height < 288 || Width > 3456 || Height > 3456)
            throw new InvalidDataException("Invalid page dimensions.");
        Background?.Validate();
        if (Background is not null && Version < 3) throw new InvalidDataException("PDF backgrounds require page version 3.");
        Paper?.Validate();
        if (Paper is not null && (Math.Abs(Paper.Width - Width) > 0.01 || Math.Abs(Paper.Height - Height) > 0.01))
            throw new InvalidDataException("Paper dimensions do not match the page.");
        if (Texts is not null)
        {
            if (Texts.Count > 2000) throw new InvalidDataException("Too many text objects.");
            var textIds = new HashSet<Guid>();
            foreach (var text in Texts)
                if (text is null || text.Id == Guid.Empty || !textIds.Add(text.Id) || !double.IsFinite(text.X) || !double.IsFinite(text.Y) ||
                    Math.Abs(text.X) > 100000 || Math.Abs(text.Y) > 100000 || !double.IsFinite(text.Width) || text.Width < 40 || text.Width > 3456 ||
                    !double.IsFinite(text.FontSize) || text.FontSize < 8 || text.FontSize > 96 || !PaperSettings.IsColor(text.Color) ||
                    text.Text is null || text.Text.Length > 100000 || text.CalculationSource is { Length: > 12000 })
                    throw new InvalidDataException("Invalid text object.");
        }
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
                !double.IsFinite(stroke.Width) || stroke.Width < 0.5 || stroke.Width > 100 ||
                (stroke.Height is double height && (!double.IsFinite(height) || height < 0.5 || height > 100)) ||
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
