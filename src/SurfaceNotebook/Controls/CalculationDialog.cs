using System.Windows;
using System.Windows.Input;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Controls;

public sealed class CalculationDialog : Window
{
    public string Source => editor.Source;
    private readonly CalculationEditor editor = new();
    public CalculationDialog(IReadOnlyList<InkStroke> ink, string source = "", bool editing = false)
    {
        Title = editing ? "Edit calculation" : "Review calculation";
        Width = Math.Min(520, SystemParameters.WorkArea.Width);
        Height = Math.Min(690, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(340, Width); MinHeight = Math.Min(420, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false; Content = editor;
        editor.Prepare(ink, source, editing);
        editor.Accepted += _ => DialogResult = true;
        editor.Canceled += () => DialogResult = false;
        Loaded += async (_, _) => { if (ink.Count > 0) await editor.RecognizeAsync(); };
        Closed += (_, _) => editor.Stop();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
