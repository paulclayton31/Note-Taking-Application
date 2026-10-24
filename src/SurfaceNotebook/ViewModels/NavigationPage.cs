using System.ComponentModel;
using System.Windows.Media;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.ViewModels;

public sealed class NavigationPage(PageSummary page) : INotifyPropertyChanged
{
    private ImageSource? thumbnail;
    public PageSummary Page { get; set; } = page;
    public Guid Id => Page.Id;
    public string Name => Page.Name;
    public string Detail { get; set; } = "";
    public ImageSource? Thumbnail
    {
        get => thumbnail;
        set { thumbnail = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
