using System.IO;
using System.Text.Json;
using System.Windows;

namespace SurfaceNotebook.Services;

public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);

public sealed class WindowPlacementStore(string directory)
{
    private string FilePath => Path.Combine(directory,"window-layout.json");
    public static WindowPlacement? Normalize(WindowPlacement p, Rect desktop)
    {
        if (desktop.IsEmpty || desktop.Width<100 || desktop.Height<100 ||
            !new[] {p.Left,p.Top,p.Width,p.Height}.All(double.IsFinite) || p.Width<100 || p.Height<100 || p.Width>50000 || p.Height>50000) return null;
        var width=Math.Min(p.Width,desktop.Width); var height=Math.Min(p.Height,desktop.Height);
        return p with { Width=width,Height=height,Left=Math.Clamp(p.Left,desktop.Left,desktop.Right-width),Top=Math.Clamp(p.Top,desktop.Top,desktop.Bottom-height) };
    }
    public WindowPlacement? Load(Rect desktop)
    {
        try
        {
            if (!File.Exists(FilePath) || new FileInfo(FilePath).Length>4096) return null;
            var saved=JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(FilePath));
            return saved is null ? null : Normalize(saved,desktop);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) { return null; }
    }
    public void Save(WindowPlacement placement)
    {
        try
        {
            if(Normalize(placement,new Rect(-50000,-50000,100000,100000)) is null) return;
            Directory.CreateDirectory(directory); var temporary=FilePath+".tmp";
            File.WriteAllText(temporary,JsonSerializer.Serialize(placement)); File.Move(temporary,FilePath,true);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { }
    }
}
