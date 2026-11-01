using System.Windows;
using System.Windows.Input;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook.Controls;

public sealed class GeminiCalculationDialog : Window
{
    private readonly GeminiCalculationEditor editor = new();
    private AiCalculation? accepted;
    public AiCalculation? Result => accepted;
    public GeminiCalculationDialog(byte[]? png, string question, GeminiKeyStore keys, AiCalculation? saved = null)
    {
        Title = "Calculate with Gemini · " + (typeof(GeminiCalculationDialog).Assembly.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false).OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion.Split('+')[0] ?? ""); Width = Math.Min(650, SystemParameters.WorkArea.Width);
        Height = Math.Min(850, SystemParameters.WorkArea.Height);
        MinWidth = Math.Min(360, Width); MinHeight = Math.Min(480, Height);
        WindowStartupLocation = WindowStartupLocation.CenterOwner; ShowInTaskbar = false; Content = editor;
        editor.Prepare(png, question, keys, saved);
        editor.Accepted += () => { accepted = editor.Result; DialogResult = true; };
        editor.Canceled += () => DialogResult = false;
        Closed += (_, _) => editor.Stop();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { DialogResult = false; e.Handled = true; } };
    }
}
