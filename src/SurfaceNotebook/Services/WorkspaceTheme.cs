using System.Windows;
using System.Windows.Media;

namespace SurfaceNotebook.Services;

public static class WorkspaceTheme
{
    public static void Apply(bool dark)
    {
        var colors = dark ? new Dictionary<string, string>
        {
            ["Surface"]="#20232B", ["SurfaceAlt"]="#252933", ["Workspace"]="#16191F", ["SidebarSurface"]="#1C1F26",
            ["Ink"]="#F0F1F5", ["Muted"]="#A0A7B6", ["Faint"]="#7B8495", ["Stroke"]="#353A46",
            ["Hover"]="#2E3441", ["Selected"]="#353652", ["Accent"]="#B7B3FF", ["AccentFill"]="#7670D9",
            ["AccentText"]="#FFFFFF", ["Danger"]="#F09696"
        } : new Dictionary<string, string>
        {
            ["Surface"]="#FFFFFF", ["SurfaceAlt"]="#F6F7F9", ["Workspace"]="#ECEEF2", ["SidebarSurface"]="#F7F8FA",
            ["Ink"]="#222735", ["Muted"]="#6B7384", ["Faint"]="#8C94A3", ["Stroke"]="#E2E5EC",
            ["Hover"]="#EEF0F5", ["Selected"]="#EEECFB", ["Accent"]="#655BBD", ["AccentFill"]="#6D61C5",
            ["AccentText"]="#FFFFFF", ["Danger"]="#C74753"
        };
        foreach (var (key, hex) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        Application.Current.Resources["IsDark"] = dark;
    }
}
