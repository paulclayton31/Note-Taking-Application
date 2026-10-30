using System.Windows;
using System.Windows.Controls;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckSavedColors(string root)
    {
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        try
        {
            var ink = (PenCanvas)window.FindName("InkSurface"); Collect(ink, SampleStroke());
            var original = InkMapper.Capture(ink.Strokes).Strokes.Single();
            ((Slider)window.FindName("ThicknessSlider")).Value = 5;
            ((RadioButton)window.FindName("BallStyleButton")).IsChecked = true;
            window.AddSavedColor("#123456"); window.AddSavedColor("#ABCDEF");
            window.AddSavedColor("#123456");
            Require(window.SavedColors.Count(c => c == "#FF123456") == 1, "Adding a duplicate color duplicated the swatch.");
            window.SelectSavedColor("#FFABCDEF");
            Require(ink.DefaultDrawingAttributes.Width == 5 && ink.DefaultDrawingAttributes.IgnorePressure && ink.DefaultDrawingAttributes.Color.ToString() == "#FFABCDEF",
                "Switching colors changed thickness or pressure style.");
            ((RadioButton)window.FindName("EraserButton")).IsChecked = true;
            window.SelectSavedColor("#FF123456");
            Require(((RadioButton)window.FindName("PenButton")).IsChecked == true && ink.DefaultDrawingAttributes.Width == 5, "Choosing a color did not resume the pen.");
            Require(InkMapper.Capture(ink.Strokes).Strokes.Single().Points.SequenceEqual(original.Points), "Choosing colors changed existing pressure data.");
            window.RemoveSavedColor("#FFABCDEF");
            Require(Await(window.ShutdownLibraryAsync()), "Colors did not save.");
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        try
        {
            Require(reopened.SavedColors.Contains("#FF123456") && !reopened.SavedColors.Contains("#FFABCDEF"), "Saved colors or removal did not survive restart.");
        }
        finally { Require(Await(reopened.ShutdownLibraryAsync()), "Reopened colors did not close."); }
    }
}
