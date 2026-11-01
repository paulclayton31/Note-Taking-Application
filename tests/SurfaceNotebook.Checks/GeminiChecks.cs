using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using SurfaceNotebook;
using SurfaceNotebook.Controls;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

internal static partial class Program
{
    private sealed class GeminiHandler : HttpMessageHandler
    {
        public int Requests;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public bool Clarify, Block, Malformed, InvalidSteps, Circuit;
        public string? Body, Key, Url;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Requests++; Url = request.RequestUri!.AbsoluteUri;
            Key = request.Headers.GetValues("x-goog-api-key").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellation);
            if (Block) await Task.Delay(Timeout.Infinite, cancellation);
            var result = new { problem = "x = 2; x + 3", answer = Clarify ? "" : Circuit ? "18 V" : "5", explanation = Clarify ? "Is the handwritten value 2 or 7?" : "Substitute x: 2 + 3 = 5.", circuit = Circuit ? new DcCircuit([new("s","a",2),new("a","0",4),new("a","b",2),new("b","0",6)], [new("s","0",8)], [new("0","a",6)], "b","0") : null, needsClarification = Clarify, steps = Clarify ? Array.Empty<object>() : new object[] { new { title = InvalidSteps ? "" : "Substitute the known value", detail = "Use x = 2 in the expression.", equations = new[] { "x + 3 = 2 + 3", "2 + 3 = 5" } } } };
            var envelope = new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = JsonSerializer.Serialize(result) } } } } } };
            return new HttpResponseMessage(Status) { Content = new StringContent(Malformed ? "{}" : JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json") };
        }
    }
    private static void CheckGemini(string root)
    {
        Directory.CreateDirectory(root);
        var touchViewport = new PageViewport(); int holds=0;
        touchViewport.TouchHeld += _ => holds++;
        touchViewport.BeginContact(1,new Point(100,100)); touchViewport.CheckTouchHold(Environment.TickCount64+700);
        Require(holds==1,"Finger hold did not open page actions."); touchViewport.EndContact(1);
        touchViewport.BeginContact(1,new Point(100,100)); touchViewport.MoveContact(1,new Point(130,130)); touchViewport.CheckTouchHold(Environment.TickCount64+700);
        Require(holds==1,"Scrolling triggered a hold menu."); touchViewport.EndContact(1);
        touchViewport.BeginContact(1,new Point(100,100)); touchViewport.BeginContact(2,new Point(200,200)); touchViewport.CheckTouchHold(Environment.TickCount64+700);
        Require(holds==1,"Pinch triggered a hold menu."); touchViewport.EndContact(1); touchViewport.EndContact(2);
        touchViewport.SetPenPresence(true); touchViewport.BeginContact(1,new Point(100,100)); touchViewport.CheckTouchHold(Environment.TickCount64+700);
        Require(holds==1,"Palm contact during pen input triggered a hold menu.");
        var graph = new DcCircuit(
            [new("s","a",2),new("a","0",4),new("a","b",2),new("b","0",6)],
            [new("s","0",8)], [new("0","a",6)], "b","0");
        var circuitResult = DcCircuitSolver.Solve(graph);
        Require(circuitResult.Answer.Contains("8.57143") && circuitResult.Steps.Any(s => s.Equations.Any(e => e.Contains("3.42857"))) &&
            circuitResult.Steps.Any(s => s.Equations.Any(e => e.Contains("5.14286"))), "Circuit regression: node/superposition did not give 60/7 V.");
        var formula = new MathDisplay { Formula = @"V_{0}=\frac{60}{7}\,\mathrm{V}\approx8.57\,\mathrm{V}" };
        formula.Measure(new Size(500,200)); formula.Arrange(new Rect(0,0,500,200));
        Require(formula.Content is ScrollViewer, "Fraction/subscript renderer fell back to raw text.");
        var power = new MathDisplay { Formula = @"x^{2}+\frac{1}{2}" };
        Require(power.Content is ScrollViewer, "Exponent renderer failed.");
        var card = new AiCalculation(GeminiCalculator.Model,"Circuit",circuitResult.Answer,"Locally calculated", "", Steps:circuitResult.Steps);
        card.Validate(); Require(SolutionCardRenderer.Render(card,540).PixelHeight>100, "Formatted note card did not render.");
        var original = InkMapper.Capture(new StrokeCollection { SampleStroke() });
        var png = CalculationCapture.Create(original, new Rect(60, 70, 160, 100));
        var image = PageImageSource.Decode(png);
        Require(image.PixelWidth == 320 && image.PixelHeight == 200, "Calculation crop lost page-space resolution.");
        var handler = new GeminiHandler(); using var client = new HttpClient(handler); var service = new GeminiCalculator(client);
        var keys = new GeminiKeyStore(Path.Combine(root, "keys")); const string testKey = "test-key-never-sent-to-google";
        keys.Save(testKey); Require(keys.Load() == testKey, "Encrypted API-key storage did not round-trip.");
        Require(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "keys", "gemini-key.bin"))).Contains(testKey), "API key was stored as plain text.");
        var editor = new GeminiCalculationEditor(); editor.Prepare(png, "Find x + 3", keys, service: service);
        Require(handler.Requests == 0 && editor.Result is null, "Opening a calculation uploaded notes automatically.");
        Await(editor.SolveAsync());
        Require(editor.Result?.Answer == "5" && ((Button)editor.FindName("AcceptButton")).IsEnabled, "Gemini solution could not be reviewed or added.");
        Require(handler.Key == testKey && !handler.Url!.Contains(testKey) && handler.Url.Contains(GeminiCalculator.Model), "Request exposed a key in its URL or used an unexpected model.");
        using (var body = JsonDocument.Parse(handler.Body!))
        {
            var part = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0];
            Require(Convert.FromBase64String(part.GetProperty("inlineData").GetProperty("data").GetString()!).SequenceEqual(png), "The sent image differed from the preview.");
            Require(!handler.Body!.Contains(testKey), "API key leaked into model input.");
        }
        editor.Measure(new Size(600, 820)); editor.Arrange(new Rect(0, 0, 600, 820)); editor.UpdateLayout();
        handler.Circuit = true; Await(editor.SolveAsync());
        Require(editor.Result!.Answer.Contains("8.57143") && !editor.Result.Answer.Contains("18"), "AI's contradictory 18 V overrode the local circuit result.");
        handler.Circuit = false; Await(editor.SolveAsync());
        var saved = editor.Result!;
        Require(saved.Steps?.Length == 1 && ((ItemsControl)editor.FindName("StepsList")).Items.Count == 1 &&
            saved.DisplayText.Contains("1. Substitute the known value\n") && saved.DisplayText.Contains("x + 3 = 2 + 3\n2 + 3 = 5"),
            "Worked solution lost step boundaries or separate equations.");
        var reopened = new GeminiCalculationEditor(); reopened.Prepare(png, saved.Instruction, keys, saved, service);
        Require(((ItemsControl)reopened.FindName("StepsList")).Items.Count == 1 && handler.Requests == 3, "Saved steps did not reopen offline.");
        var legacy = saved with { Steps = null };
        reopened.Prepare(png, legacy.Instruction, keys, legacy, service);
        Require(((TextBox)reopened.FindName("ExplanationText")).Text == legacy.Explanation, "Older saved explanations became unreadable.");
        ((TextBox)editor.FindName("QuestionBox")).Text = "Actually x = 7";
        Require(editor.Result is null && !((Button)editor.FindName("AcceptButton")).IsEnabled, "Changing the question left an outdated result addable.");
        handler.Clarify = true; Await(editor.SolveAsync());
        Require(editor.Result is null && ((TextBox)editor.FindName("ExplanationText")).Text.Contains("2 or 7"), "Ambiguous handwriting was silently accepted.");
        handler.Clarify = false; handler.Status = HttpStatusCode.TooManyRequests;
        var calls = handler.Requests; Await(editor.SolveAsync());
        Require(handler.Requests == calls + 1 && editor.Result is null && ((TextBlock)editor.FindName("ErrorText")).Text.Contains("usage limit"), "Quota errors retried, switched models, or left a stale result.");
        handler.Status = HttpStatusCode.Forbidden; Await(editor.SolveAsync());
        Require(((TextBlock)editor.FindName("ErrorText")).Text.Contains("key"), "Invalid credentials were not explained.");
        handler.Status = HttpStatusCode.OK; handler.Malformed = true; Await(editor.SolveAsync());
        Require(editor.Result is null && !((Button)editor.FindName("AcceptButton")).IsEnabled, "Malformed Gemini output became a solution.");
        handler.Malformed = false; handler.InvalidSteps = true; Await(editor.SolveAsync());
        Require(editor.Result is null && ((FrameworkElement)editor.FindName("ResultPanel")).Visibility == Visibility.Collapsed,
            "Invalid structured steps were displayed as a completed solution.");
        handler.InvalidSteps = false; handler.Block = true;
        using (var canceled = new CancellationTokenSource())
        {
            canceled.Cancel();
            try { Await(service.SolveAsync(testKey, png, "Solve", canceled.Token)); throw new Exception("Canceled Gemini request succeeded."); }
            catch (OperationCanceledException) { }
        }
        var pending = editor.SolveAsync(); editor.Stop(); Await(pending);
        Require(editor.Result is null, "Closing a review accepted a late response.");
        keys.Delete(); Require(keys.Load() == "", "Forget key did not remove the credential.");

        var window = new MainWindow(Path.Combine(root, "library")); Await(window.InitializeLibraryAsync()); window.ShowWorkspace(); ArrangeWorkspace(window);
        var ink = (PenCanvas)window.FindName("InkSurface"); var page = window.CurrentPageId!.Value;
        try
        {
            ((RadioButton)window.FindName("CalculateButton")).IsChecked = true;
            Click((Button)window.FindName("SelectCalculationAreaButton"));
            Require(((FrameworkElement)window.FindName("CalculationAreaOverlay")).Visibility == Visibility.Visible, "Calculate could not select a PDF/image area.");
            ((RadioButton)window.FindName("PenButton")).IsChecked = true;
            Require(((FrameworkElement)window.FindName("CalculationAreaOverlay")).Visibility == Visibility.Collapsed && ink.EditingMode == InkCanvasEditingMode.Ink,
                "Switching tools left the area selection overlay active.");
            Collect(ink, SampleStroke()); var originalId = InkMapper.Identity(ink.Strokes.Single());
            window.SaveAiCalculation(saved, new Point(80, 250));
            Require(ink.Children.OfType<TextBox>().Single().Text.Contains("Answer") && ink.Children.OfType<TextBox>().Single().Text.Contains("5") && InkMapper.Identity(ink.Strokes.Single()) == originalId, "Adding an AI solution changed handwriting or lost the result.");
            Click((Button)window.FindName("UndoButton")); Require(ink.Children.Count == 0 && ink.Strokes.Count == 1, "AI result did not undo independently.");
            Click((Button)window.FindName("RedoButton"));
            ((RadioButton)window.FindName("LassoButton")).IsChecked = true;
            ink.Select(new StrokeCollection(), ink.Children.Cast<UIElement>()); window.DuplicateSelectedObjects();
            Require(ink.Children.Count == 2, "AI card could not be duplicated.");
            Click((Button)window.FindName("UndoButton"));
            Require(Await(window.FlushAsync()) && Await(window.ShutdownLibraryAsync()), "AI result failed to save.");
            using var db = new NotebookDatabase(Path.Combine(root, "library"));
            var document = db.LoadPage(page); var result = document.Texts!.Single().AiCalculation!;
            Require(result.Steps?.Single().Equations.Length == 2, "Saving lost structured solution steps.");
            Require(result.Answer == "5" && result.ImagePng!.SequenceEqual(png), "Saved result lost its source image or answer.");
            var portable = Path.Combine(root, "ai.stempage"); Await(PageStore.SaveAsync(portable, document));
            Require(Await(PageStore.LoadAsync(portable)).Texts!.Single().AiCalculation!.Explanation == saved.Explanation, "Portable export lost the AI explanation.");
            Await(PdfService.ExportAsync(Path.Combine(root, "ai.pdf"), [document], "AI solution")); db.MarkClean();
        }
        finally { Await(window.ShutdownLibraryAsync()); }
    }
}

