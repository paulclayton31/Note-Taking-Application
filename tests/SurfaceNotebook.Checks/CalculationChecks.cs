using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Services;

internal static partial class Program
{
    // A reproducible ink fixture, separate from real-device recognition accuracy.
    private static StrokeCollection CalculationHandwritingFixture()
    {
        var strokes = new StrokeCollection();
        void Path(double x, double y, params double[] coordinates) => strokes.Add(new Stroke(new StylusPointCollection(
            Enumerable.Range(0, coordinates.Length / 2).Select(i => new StylusPoint(x + coordinates[i * 2], y + coordinates[i * 2 + 1])))));
        void X(double y) { Path(10, y, 0, 0, 32, 40); Path(10, y, 32, 0, 0, 40); }
        void Y(double x, double y) { Path(x, y, 0, 0, 14, 22, 28, 0); Path(x, y, 14, 22, 10, 36, 3, 48); }
        void Equals(double y) { Path(70, y, 0, 14, 28, 14); Path(70, y, 0, 25, 28, 25); }
        X(10); Equals(10); Path(120, 10, 0, 8, 6, 0, 21, 0, 29, 9, 27, 18, 0, 42, 31, 42);
        Y(10, 100); Equals(100); Path(120, 100, 0, 6, 12, 0, 27, 5, 28, 14, 15, 21, 28, 25, 30, 35, 22, 44, 5, 43, 0, 38);
        X(190); Path(70, 190, 0, 20, 28, 20); Path(70, 190, 14, 6, 14, 34); Y(120, 190);
        return strokes;
    }
    private static void CheckCalculations(string root)
    {
        const string source = "F = m * a\nm = 2\na = 3";
        var window = new MainWindow(root); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        var first = window.CurrentPageId!.Value;
        Guid card, duplicate;
        var ink = (PenCanvas)window.FindName("InkSurface");
        Collect(ink, SampleStroke());
        var original = InkMapper.Capture(ink.Strokes).Strokes.Single();
        try
        {
            ((RadioButton)window.FindName("CalculateButton")).IsChecked = true;
            Require(ink.EditingMode == InkCanvasEditingMode.Select && ((FrameworkElement)window.FindName("CalculationActionsPanel")).Visibility == Visibility.Visible,
                "Calculate did not expose its own selection workflow.");
            ink.Select(new StrokeCollection { ink.Strokes[0] });
            Require(((Button)window.FindName("ReviewCalculationButton")).IsEnabled && ((Button)window.FindName("SelectionActionsButton")).Visibility == Visibility.Collapsed,
                "Calculate selection was mixed with lasso actions.");
            card = window.SaveCalculation(source, new Point(80, 260));
            var box = ink.Children.OfType<TextBox>().Single();
            Require(box.Text.Contains("→ 6") && box.IsReadOnly && (Guid)box.Tag == card, "A calculation was not displayed as a protected result card.");
            Require(InkMapper.Identity(ink.Strokes.Single()) == original.Id && InkMapper.Capture(ink.Strokes).Strokes.Single().Points.SequenceEqual(original.Points),
                "Recognition or calculation changed the original handwriting or pressure.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Children.Count == 0 && ink.Strokes.Count == 1, "Adding a calculation did not undo independently of its source ink.");
            Click((Button)window.FindName("RedoButton")); Require(ink.Children.OfType<TextBox>().Single().Text.Contains("→ 6"), "Calculation redo lost the result.");
            window.SaveCalculation(source.Replace("m = 2", "m = 4"), new Point(), card);
            Require(ink.Children.OfType<TextBox>().Single().Text.Contains("→ 12"), "Changing a variable did not recompute the formula.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Children.OfType<TextBox>().Single().Text.Contains("→ 6"), "Undo did not restore the earlier formula and result.");
            try { window.SaveCalculation("F = 1/0", new Point(), card); throw new Exception("Invalid calculation changed a card."); }
            catch (CalculationEngine.CalculationException) { }
            Require(ink.Children.OfType<TextBox>().Single().Text.Contains("→ 6"), "Rejected input damaged the saved result.");
            ((RadioButton)window.FindName("LassoButton")).IsChecked = true;
            ink.Select(new StrokeCollection(), ink.Children.Cast<UIElement>());
            window.DuplicateSelectedObjects();
            Require(ink.Children.Count == 2 && ink.Children.OfType<TextBox>().Select(b => (Guid)b.Tag).Distinct().Count() == 2,
                "A duplicated calculation reused its source identity.");
            Click((Button)window.FindName("UndoButton"));
            duplicate = Await(window.DuplicatePageAsync(first));
            var duplicateCard = (Guid)ink.Children.OfType<TextBox>().Single().Tag;
            Require(duplicateCard != card, "Page duplication reused the calculation identity.");
            window.SaveCalculation("F = m * a\nm = 10\na = 3", new Point(), duplicateCard);
            Await(window.ActivatePageAsync(first));
            Require(ink.Children.OfType<TextBox>().Single().Text.Contains("→ 6"), "Editing a copied calculation changed the source page.");
            ((RadioButton)window.FindName("TextButton")).IsChecked = true;
            Require(ink.Children.OfType<TextBox>().Single().IsReadOnly, "The Text tool could overwrite a calculation's cached result.");
            ((RadioButton)window.FindName("CalculateButton")).IsChecked = true;
            window.SetBarrelSelection(true);
            Require(ink.EditingMode == InkCanvasEditingMode.Select && ((FrameworkElement)window.FindName("CalculationActionsPanel")).Visibility == Visibility.Collapsed,
                "Barrel lasso left Calculate actions active.");
            window.SetBarrelSelection(false);
            Require(((RadioButton)window.FindName("CalculateButton")).IsChecked == true && ((FrameworkElement)window.FindName("CalculationActionsPanel")).Visibility == Visibility.Visible,
                "Barrel lasso did not restore Calculate.");
            var editor = new CalculationEditor(); editor.Prepare([], source, false);
            Require(editor.Result?.Rows[0].Value == 6 && ((Button)editor.FindName("AcceptButton")).IsEnabled, "The review editor did not preview the calculation.");
            editor.Source = "F = missing + 1";
            Require(!((Button)editor.FindName("AcceptButton")).IsEnabled, "An outdated result could be accepted after editing.");
            editor.RefreshResult(); Require(editor.Result is null && ((TextBlock)editor.FindName("ErrorText")).Text.Contains("Unknown variable"), "The review editor hid invalid math.");
            editor.Stop();
            var recognition = Await(HandwritingRecognition.RecognizeAsync(InkMapper.Capture(CalculationHandwritingFixture()).Strokes).WaitAsync(TimeSpan.FromSeconds(25)));
            Console.WriteLine($"Handwriting service: available={recognition.RecognizerAvailable}; text='{recognition.Text}'; {recognition.Message}");
            if (recognition.RecognizerAvailable)
            {
                Require(!string.IsNullOrWhiteSpace(recognition.Text), "Windows returned no text for the handwritten variable fixture.");
                try { Console.WriteLine($"Handwriting fixture calculation (before human review): {CalculationEngine.Evaluate(recognition.Text).DisplayText}"); }
                catch (CalculationEngine.CalculationException ex) { Console.WriteLine($"Handwriting fixture requires review: {ex.Message}"); }
            }
            using (var canceled = new CancellationTokenSource())
            {
                canceled.Cancel();
                try { Await(HandwritingRecognition.RecognizeAsync([original], canceled.Token)); throw new Exception("Canceled recognition completed."); }
                catch (OperationCanceledException) { }
            }
            Require(Await(window.FlushAsync()), "Calculations did not flush.");
            var portable = Path.Combine(root, "calculation.stempage");
            // Read on the ordered storage worker by closing first, just as an actual restart would.
            Require(Await(window.ShutdownLibraryAsync()), "Calculation library did not close.");
            using (var db = new NotebookDatabase(root))
            {
                var document = db.LoadPage(first);
                Require(document.Texts!.Single().CalculationSource == source && document.Strokes.Single().Points.SequenceEqual(original.Points),
                    "Storage lost the reviewed source or changed handwriting.");
                Await(PageStore.SaveAsync(portable, document));
                Require(Await(PageStore.LoadAsync(portable)).Texts!.Single().CalculationSource == source, "Portable pages lost editable calculation formulas.");
                Require(db.LoadPage(duplicate).Texts!.Single().CalculationSource!.Contains("m = 10"), "A copied card lost its independent source.");
                db.MarkClean();
            }
        }
        finally { Await(window.ShutdownLibraryAsync()); }
        var reopened = new MainWindow(root); Await(reopened.InitializeLibraryAsync()); reopened.ShowWorkspace();
        try
        {
            var reopenedInk = (PenCanvas)reopened.FindName("InkSurface");
            Require(reopened.CurrentPageId == first && reopenedInk.Children.OfType<TextBox>().Single().Text.Contains("→ 6"), "Restart lost the calculation card.");
            reopened.SaveCalculation("F = m * a\nm = 5\na = 3", new Point(), card);
            Require(reopenedInk.Children.OfType<TextBox>().Single().Text.Contains("→ 15"), "A reopened calculation could not be edited.");
            var pdf = Path.Combine(root, "calculation.pdf"); Await(reopened.ExportPdfAsync(pdf, entireNotebook: false));
            Require(Await(PdfService.ImportAsync(pdf)).Count == 1, "Calculation PDF export did not render.");
        }
        finally { Require(Await(reopened.ShutdownLibraryAsync()), "Reopened calculation library did not close."); }
    }
}
