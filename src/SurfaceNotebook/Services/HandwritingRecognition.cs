using System.Numerics;
using System.Runtime.InteropServices;
using Windows.UI.Input.Inking;
using InkStroke = SurfaceNotebook.Models.InkStroke;

namespace SurfaceNotebook.Services;

// Only explicitly selected, copied ink reaches Windows recognition. This never
// changes live WPF strokes or sends notebook content to a network service.
public static class HandwritingRecognition
{
    public sealed record Result(string Text, string Message, bool RecognizerAvailable);
    private sealed record Word(string Text, double X, double Y, double Height);
    public static Task<Result> RecognizeAsync(IReadOnlyList<InkStroke> strokes, CancellationToken cancellation = default)
    {
        // Recognition has its own bounded task; callers may close the review
        // window without waiting or accepting a late result.
        return Task.Run(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (strokes.Count == 0) return new Result("", "Circle handwriting first, or type your calculation below.", true);
            if (strokes.Count > 1000 || strokes.Sum(s => (long)s.Points.Count) > 100000)
                return new Result("", "Select a smaller calculation (up to 1,000 strokes / 100,000 points), or type it below.", true);
            try
            {
                var recognizer = new InkRecognizerContainer();
                var available = recognizer.GetRecognizers();
                if (available.Count == 0) return Unavailable();
                var container = new InkStrokeContainer();
                var builder = new InkStrokeBuilder();
                // Translation and a generous scale retain small handwriting at any viewport zoom.
                var left = strokes.Min(s => s.Points.Min(p => p.X)); var top = strokes.Min(s => s.Points.Min(p => p.Y));
                foreach (var stroke in strokes)
                {
                    cancellation.ThrowIfCancellationRequested();
                    container.AddStroke(builder.CreateStrokeFromInkPoints(stroke.Points.Select(p => new Windows.UI.Input.Inking.InkPoint(
                        new Windows.Foundation.Point(p.X - left + 16, p.Y - top + 16), p.Pressure)), Matrix3x2.Identity));
                }
                var recognized = await recognizer.RecognizeAsync(container, InkRecognitionTarget.All).AsTask(cancellation);
                var words = recognized.Select(r => new Word(r.GetTextCandidates().FirstOrDefault() ?? "", r.BoundingRect.X,
                    r.BoundingRect.Y, r.BoundingRect.Height)).Where(w => !string.IsNullOrWhiteSpace(w.Text)).OrderBy(w => w.Y).ToList();
                var rows = new List<List<Word>>();
                foreach (var word in words)
                {
                    var row = rows.LastOrDefault();
                    if (row is null || Math.Abs(word.Y + word.Height / 2 - row.Average(w => w.Y + w.Height / 2)) >
                        Math.Max(12, Math.Max(word.Height, row.Average(w => w.Height)) * .65))
                        rows.Add([word]);
                    else row.Add(word);
                }
                var text = string.Join(Environment.NewLine, rows.Select(row => string.Join(" ", row.OrderBy(w => w.X).Select(w => w.Text))));
                if (text.Length > CalculationEngine.MaxSourceLength)
                    return new Result("", "The recognized text is too long. Circle a smaller calculation, or type it below.", true);
                return new Result(text, text.Length == 0 ? "No handwriting was recognized. Try a smaller selection, or type below."
                    : "Check every variable, number, and operator before adding the result. Handwriting recognition can make mistakes.", true);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (ex is COMException or InvalidOperationException or NotSupportedException)
            { return Unavailable(); }
        }, cancellation);
    }
    private static Result Unavailable() => new("", "Windows handwriting recognition is unavailable. In Windows Settings → Time & language → Language options, install Handwriting for your language, then reopen the app. You can still type and calculate below.", false);
}
