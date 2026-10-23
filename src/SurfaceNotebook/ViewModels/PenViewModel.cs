using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SurfaceNotebook.ViewModels;

public sealed class PenViewModel : INotifyPropertyChanged
{
    private double thickness = 2.5;
    private string status = "Ready · use your pen to write";
    public double Thickness { get => thickness; set { thickness = value; Changed(); } }
    public string Status { get => status; set { status = value; Changed(); } }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
