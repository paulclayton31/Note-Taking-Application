using System.Windows;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.ViewModels;

public sealed record HomeNotebookItem(NotebookSummary Notebook, string Detail, string Location, string Accent,
    string Tint, ImageSource? Thumbnail)
{
    public Guid Id => Notebook.Id;
    public string Name => Notebook.Name;
    public Visibility StarVisibility => Notebook.Favorite ? Visibility.Visible : Visibility.Collapsed;
}
public sealed record HomeFolderItem(Guid Id, string Name, string Detail, int Depth = 0)
{
    public Thickness Indent => new(8 + Depth * 14, 0, 0, 0);
}
public sealed record HomeBreadcrumb(Guid? Id, string Name);
public sealed record FolderDestination(Guid? Id, string Name);
public sealed record PresetItem(PenPreset Preset, bool Selected)
{
    public Guid Id => Preset.Id;
    public string Name => Preset.Name;
    public string Color => Preset.Color;
    public string Description => Preset.Description;
    public string Icon => Preset.Monoline ? "BallPen" : "FountainPen";
    public string PreviewPaper
    {
        get
        {
            var color = (Color)ColorConverter.ConvertFromString(Preset.Color);
            return .2126 * color.R + .7152 * color.G + .0722 * color.B > 160 ? "#FF252B36" : "#FFFFFEFA";
        }
    }
}
