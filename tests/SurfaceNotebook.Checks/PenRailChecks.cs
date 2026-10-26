using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Ink;
using System.Windows.Media;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private static void CheckPenRail(string root)
    {
        var legacyColor = "#FF7843A8";
        using (var db = new NotebookDatabase(root))
        {
            db.SavePreferences(new WorkspacePreferences(HomeVisible: false, Writing: new WritingPreferences(
                Presets: PenPreset.Defaults, ActivePreset: null, PenColor: legacyColor, PenWidth: 3.5, Monoline: true)));
            db.MarkClean();
        }
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace();
        var ink = (PenCanvas)window.FindName("InkSurface"); var slider = (Slider)window.FindName("ThicknessSlider");
        var recovered = window.SavedPens.Single(p => p.Color == legacyColor);
        Require(recovered.Width == 3.5 && recovered.Monoline && ink.DefaultDrawingAttributes.IgnorePressure,
            "The detached v0.3.0 current pen was not recovered.");
        var shutdownMode = Application.Current.ShutdownMode; Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        window.ShowActivated = false; window.ShowInTaskbar = false; window.Left = -30000; window.Top = -30000;
        window.Width = 1440; window.Height = 1000; window.Show(); ArrangeWorkspace(window, 1440, 1000);
        var popup = (Popup)window.FindName("PenEditorPopup"); ((FrameworkElement)popup.Child).Opacity = 0;
        var allPens = (Popup)window.FindName("AllPensPopup"); ((FrameworkElement)allPens.Child).Opacity = 0;
        Guid addedId;
        string addedColor;
        try
        {
            slider.Value = 5;
            ((RadioButton)window.FindName("FountainStyleButton")).IsChecked = true;
            Require(window.SetInkColor("#234567"), "Pen recoloring failed.");
            var edited = window.SavedPens.Single(p => p.Id == recovered.Id);
            Require(edited.Color == "#FF234567" && edited.Width == 5 && !edited.Monoline, "Pen edits did not update the selected pen.");
            window.ApplyPenPreset(PenPreset.Defaults[0].Id); window.ApplyPenPreset(recovered.Id);
            Require(ink.DefaultDrawingAttributes.Width == 5 && !ink.DefaultDrawingAttributes.IgnorePressure && ink.DefaultDrawingAttributes.Color.ToString() == edited.Color,
                "Switching pens lost edits.");
            ((RadioButton)window.FindName("HighlighterButton")).IsChecked = true;
            window.SetInkColor("#FFFF00"); slider.Value = 36;
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            Require(ink.DefaultDrawingAttributes.Width == 5 && ink.DefaultDrawingAttributes.Color.ToString() == edited.Color,
                "Highlighter changes contaminated the selected pen.");

            var wheel = (ColorWheel)window.FindName("PenColorWheel");
            var count = window.SavedPens.Count;
            window.BeginAddPen(); ((FrameworkElement)popup.Child).UpdateLayout();
            wheel.SelectedColor = Colors.Red;
            Require(window.SavedPens.Count == count && window.SavedPens.Single(p => p.Id == recovered.Id) == edited,
                "Choosing a new pen's color modified an existing pen.");
            popup.IsOpen = false;
            Require(window.SavedPens.Count == count, "Canceling Add pen created a pen.");
            window.BeginAddPen(); ((FrameworkElement)popup.Child).UpdateLayout();
            ((TextBox)window.FindName("NewPenName")).Text = "Blue ball";
            ((RadioButton)window.FindName("NewBallButton")).IsChecked = true;
            wheel.SelectedColor = Colors.DeepSkyBlue;
            Click((Button)window.FindName("CommitPenButton"));
            var added = window.SavedPens.Single(p => p.Name == "Blue ball"); addedId = added.Id;
            Require(window.SavedPens.Count == count + 1 && added.Monoline && added.Color == Colors.DeepSkyBlue.ToString() && !popup.IsOpen,
                "Add pen did not commit the wheel color and Ball style.");
            Click((Button)window.FindName("RailColorButton")); ((FrameworkElement)popup.Child).UpdateLayout();
            wheel.SelectedColor = Colors.Teal;
            ((Slider)window.FindName("PenBrightnessSlider")).Value = .5;
            addedColor = wheel.SelectedColor.ToString();
            Require(window.SavedPens.Single(p => p.Id == addedId).Color == addedColor, "Wheel edits did not save to the selected pen.");
            Click((Button)window.FindName("CommitPenButton"));
            slider.Value = 4.5;
            ((RadioButton)window.FindName("FountainStyleButton")).IsChecked = true;
            Require(!window.SavedPens.Single(p => p.Id == addedId).Monoline, "Fountain button did not save pressure style.");
            window.ShowToolInspector(); ArrangeWorkspace(window, 1440, 1000);
            Require(slider.Orientation == Orientation.Vertical && Descendants<Track>(slider).Single().Orientation == Orientation.Vertical,
                "The thickness slider template is still horizontal.");
            Slider.IncreaseLarge.Execute(null, slider);
            Require(slider.Value > 4.5 && window.SavedPens.Single(p => p.Id == addedId).Width == slider.Value, "Vertical thickness changes did not save.");
            slider.Value = 4.5;

            foreach (var size in new[] { new Size(1440, 1000), new Size(960, 1440), new Size(640, 560), new Size(720, 700) })
            {
                window.CloseToolInspector(); window.Width = size.Width; window.Height = size.Height;
                ArrangeWorkspace(window, size.Width, size.Height);
                var stage = (FrameworkElement)window.FindName("MainStage"); var width = stage.ActualWidth;
                window.ShowToolInspector(); ArrangeWorkspace(window, size.Width, size.Height);
                var rail = (FrameworkElement)window.FindName("ToolInspector");
                var bounds = rail.TransformToAncestor(stage).TransformBounds(new Rect(rail.RenderSize));
                var more = (Button)window.FindName("RailOptionsButton");
                var moreBounds = more.TransformToAncestor(stage).TransformBounds(new Rect(more.RenderSize));
                Require(rail.ActualWidth == 64 && stage.ActualWidth == width && bounds.Right <= stage.ActualWidth && bounds.Bottom <= stage.ActualHeight &&
                    moreBounds.Bottom <= stage.ActualHeight - 11 && slider.ActualHeight >= 96,
                    $"The tool rail clipped controls or narrowed the page at {size}: rail {bounds}, more {moreBounds}, stage {stage.RenderSize}.");
            }
            window.Width = 640; window.Height = 560; ArrangeWorkspace(window, 640, 560); window.ShowToolInspector(); ArrangeWorkspace(window, 640, 560);
            Click((Button)window.FindName("RailPensButton"));
            Require(allPens.IsOpen && ((ItemsControl)window.FindName("AllPenList")).Items.Count == window.SavedPens.Count,
                "The compact rail did not expose all pens.");
            allPens.IsOpen = false;
            Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "Pen edits did not save before closing.");
        }
        finally
        {
            Await(window.ShutdownLibraryAsync()); window.Close(); Application.Current.ShutdownMode = shutdownMode;
        }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync());
        var restored = reopened.SavedPens.Single(p => p.Id == addedId);
        Require(restored.Color == addedColor && restored.Width == 4.5 && !restored.Monoline &&
            reopened.SavedPens.Single(p => p.Id == recovered.Id).Color == "#FF234567", "Restart lost independent pen edits or recovered identity.");
        reopened.ApplyPenPreset(addedId); reopened.RemovePenPreset(addedId);
        Require(reopened.SavedPens.All(p => p.Id != addedId), "Removing the active pen failed.");
        foreach (var pen in reopened.SavedPens.Skip(1).ToList()) reopened.RemovePenPreset(pen.Id);
        reopened.RemovePenPreset(reopened.SavedPens[0].Id);
        Require(reopened.SavedPens.Count == 1, "Removing the last pen left no editable pen.");
        Require(Await(reopened.ShutdownLibraryAsync()), "The reopened pen library did not close.");
    }
    private static void CheckColorWheel()
    {
        foreach (var color in new[] { Colors.Black, Colors.White, Colors.Red, Colors.Cyan, Colors.Teal, Color.FromArgb(128, 94, 63, 177) })
        {
            var hsv = ColorWheel.ToHsv(color); var result = ColorWheel.FromHsv(hsv.Hue, hsv.Saturation, hsv.Value, color.A);
            Require(Math.Abs(color.R - result.R) <= 1 && Math.Abs(color.G - result.G) <= 1 && Math.Abs(color.B - result.B) <= 1 && color.A == result.A,
                "The color wheel changed an existing ink color on conversion.");
        }
        var wheel = new ColorWheel { SelectedColor = Colors.White };
        wheel.Measure(new Size(224, 224)); wheel.Arrange(new Rect(0, 0, 224, 224));
        var finger = new Finger(501) { Position = new Point(214, 112) };
        finger.Send(wheel, UIElement.TouchDownEvent);
        Require(wheel.SelectedColor.R == 255 && wheel.SelectedColor.G == 0 && wheel.SelectedColor.B == 0, "Touch did not select red on the color wheel.");
        finger.Position = new Point(112, 112); finger.Send(wheel, UIElement.TouchMoveEvent); finger.Send(wheel, UIElement.TouchUpEvent);
        Require(wheel.SelectedColor == Colors.White, "The wheel center did not select white.");
        wheel.ChooseAt(new Point(10, 112));
        Require(wheel.SelectedColor == Colors.Cyan, "The wheel did not select the opposite hue.");
        wheel.Brightness = 0; Require(wheel.SelectedColor == Colors.Black, "Brightness did not allow black ink.");
    }
}
