using System.Globalization;
using System.IO;

namespace SurfaceNotebook.Models;

public enum PaperFormat { Letter, A4, Legal, Custom }
public enum PaperPattern { Blank, College, Wide, Graph, Dots, Engineering }

// Physical dimensions use 96 device-independent pixels per inch, at every zoom.
public sealed record PaperSettings(
    PaperFormat Format = PaperFormat.Letter,
    bool Landscape = false,
    PaperPattern Pattern = PaperPattern.College,
    double CustomWidth = 8.5,
    double CustomHeight = 11,
    double Spacing = 24,
    string Background = "#FFFFFEFA",
    string LineColor = "#FFDCE3EC")
{
    public static PaperSettings Default { get; } = new();
    private (double Width, double Height) Dimensions => Format switch
    {
        PaperFormat.A4 => (210 * 96 / 25.4, 297 * 96 / 25.4),
        PaperFormat.Legal => (816, 1344),
        PaperFormat.Custom => (CustomWidth * 96, CustomHeight * 96),
        _ => (816, 1056)
    };
    public double Width => Landscape ? Dimensions.Height : Dimensions.Width;
    public double Height => Landscape ? Dimensions.Width : Dimensions.Height;
    public string Label => $"{(Format == PaperFormat.Letter ? "US Letter" : Format)} · {(Landscape ? "Landscape" : "Portrait")}";
    public double LineSpacing => Pattern switch { PaperPattern.College => 26.4, PaperPattern.Wide => 33.6, _ => Spacing };
    public static bool IsColor(string? color) => color is { Length: 9 } && color[0] == '#' &&
        uint.TryParse(color.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _);
    public void Validate()
    {
        if (!Enum.IsDefined(Format) || !Enum.IsDefined(Pattern) || !double.IsFinite(CustomWidth) || !double.IsFinite(CustomHeight) ||
            CustomWidth < 3 || CustomWidth > 36 || CustomHeight < 3 || CustomHeight > 36 ||
            !double.IsFinite(Spacing) || Spacing < 8 || Spacing > 96 || !IsColor(Background) || !IsColor(LineColor))
            throw new InvalidDataException("Paper dimensions must be 3–36 inches, spacing 8–96, and colors #AARRGGBB.");
    }
}

// CalculationSource is optional metadata in the existing objects JSON. Older apps
// still display the cached Text; no SQLite migration or new portable format is needed.
public sealed record PageText(Guid Id, double X, double Y, double Width, double FontSize, string Color, string Text,
    string? CalculationSource = null);

public sealed record WorkspacePreferences(bool Dark = false, bool Sidebar = true, bool ToolbarBottom = false,
    List<Guid>? OpenPages = null, List<Guid>? Favorites = null, List<Guid>? OpenNotebooks = null,
    Dictionary<Guid, Guid>? NotebookPages = null, ToolbarPosition? ToolbarPosition = null,
    WritingPreferences? Writing = null, bool HomeVisible = true);

public sealed record PenPreset(Guid Id, string Name, string Color, double Width, bool Monoline = false)
{
    public bool IsValid => Id != Guid.Empty && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 80 &&
        PaperSettings.IsColor(Color) && double.IsFinite(Width) && Width >= .5 && Width <= 12;
    public string Description => $"{Width:0.#} pt · {(Monoline ? "Ball · monoline" : "Fountain · pressure")}";
    public static List<PenPreset> Defaults =>
    [
        new(new Guid("31b22a24-42df-4636-8e6c-bd2367fa2e11"), "Midnight", "#FF24324A", 2.5),
        new(new Guid("31b22a24-42df-4636-8e6c-bd2367fa2e12"), "Indigo", "#FF5C55B3", 2.5),
        new(new Guid("31b22a24-42df-4636-8e6c-bd2367fa2e13"), "Fine rose", "#FFCE3F53", 1.5)
    ];
}
public sealed record WritingPreferences(List<PenPreset>? Presets = null, Guid? ActivePreset = null,
    string PenColor = "#FF24324A", double PenWidth = 2.5, bool Monoline = false,
    string HighlighterColor = "#FFE7B944", double HighlighterWidth = 18, double EraserSize = 24, bool PrecisionEraser = false,
    bool ScribbleErase = true, bool HoldShapes = true);

// Fractions of the available toolbar travel keep the palette reachable after
// rotation, resizing, and opening side panels. Legacy preferences need no migration.
public sealed record ToolbarPosition(double Horizontal = .5, double Vertical = 0)
{
    public ToolbarPosition Constrain() => new(
        double.IsFinite(Horizontal) ? Math.Clamp(Horizontal, 0, 1) : .5,
        double.IsFinite(Vertical) ? Math.Clamp(Vertical, 0, 1) : 0);
}
