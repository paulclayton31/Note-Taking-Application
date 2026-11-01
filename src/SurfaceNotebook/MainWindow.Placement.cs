using System.Windows;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private void InitializeWindowPlacement()
    {
        var store=new WindowPlacementStore(libraryDirectory);
        var desktop=new Rect(SystemParameters.VirtualScreenLeft,SystemParameters.VirtualScreenTop,SystemParameters.VirtualScreenWidth,SystemParameters.VirtualScreenHeight);
        var saved=store.Load(desktop);
        if(saved is not null)
        {
            WindowStartupLocation=WindowStartupLocation.Manual;
            MinWidth=Math.Min(MinWidth,desktop.Width); MinHeight=Math.Min(MinHeight,desktop.Height);
            saved=WindowPlacementStore.Normalize(saved with {Width=Math.Max(MinWidth,saved.Width),Height=Math.Max(MinHeight,saved.Height)},desktop)!;
            Left=saved.Left; Top=saved.Top; Width=saved.Width; Height=saved.Height;
            if(saved.Maximized) WindowState=WindowState.Maximized;
        }
        else WindowStartupLocation=WindowStartupLocation.CenterScreen;
        bool maximized=saved?.Maximized ?? false,shown=false;
        Loaded += (_,_)=>shown=true;
        StateChanged += (_,_)=> { if(WindowState!=WindowState.Minimized) maximized=WindowState==WindowState.Maximized; };
        Closed += (_,_)=> {
            if(!shown) return;
            var bounds=WindowState==WindowState.Normal ? new Rect(Left,Top,ActualWidth,ActualHeight) : RestoreBounds;
            if(!bounds.IsEmpty) store.Save(new WindowPlacement(bounds.Left,bounds.Top,bounds.Width,bounds.Height,maximized));
        };
    }
}
