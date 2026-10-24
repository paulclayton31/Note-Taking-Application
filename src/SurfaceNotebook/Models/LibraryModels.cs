namespace SurfaceNotebook.Models;

public sealed record NotebookSummary(Guid Id, string Name, PaperSettings? Paper = null);
public sealed record PageSummary(Guid Id, Guid NotebookId, string Name, int Position, PageView? View, PaperSettings? Paper = null);
public sealed record LibrarySnapshot(List<NotebookSummary> Notebooks, List<PageSummary> Pages,
    Guid LastPageId, bool RecoveredSession, WorkspacePreferences? Preferences = null);
public sealed record StoredStroke(InkStroke Stroke, int Ordinal);

// Positions are viewport DIPs; ink always remains in unscaled page coordinates.
public sealed record PageView(double Zoom, double X, double Y)
{
    public const double MinZoom = 0.25;
    public const double MaxZoom = 4;
    public PageView ZoomAt(double factor, double anchorX, double anchorY)
    {
        if (!double.IsFinite(factor) || factor <= 0) return this;
        var zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        return new PageView(zoom, anchorX - (anchorX - X) * zoom / Zoom,
            anchorY - (anchorY - Y) * zoom / Zoom);
    }
    public PageView Pan(double x, double y) => this with { X = X + x, Y = Y + y };
    public PageView Constrain(double viewportWidth, double viewportHeight, double paperWidth = PageDocument.DefaultWidth, double paperHeight = PageDocument.DefaultHeight)
    {
        var zoom = double.IsFinite(Zoom) ? Math.Clamp(Zoom, MinZoom, MaxZoom) : 1;
        return new PageView(zoom,
            Math.Clamp(double.IsFinite(X) ? X : 24, 48 - paperWidth * zoom, Math.Max(48, viewportWidth) - 48),
            Math.Clamp(double.IsFinite(Y) ? Y : 24, 48 - paperHeight * zoom, Math.Max(48, viewportHeight) - 48));
    }
}
