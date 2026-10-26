using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Threading;
using SurfaceNotebook.Models;
using SurfaceNotebook.Services;

namespace SurfaceNotebook;

public partial class MainWindow
{
    private readonly DispatcherTimer editNoticeTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool editNoticeInitialized;

    public bool TryScribbleErase(Stroke gesture)
    {
        if (busy || toolName != "Pen" || ScribbleEraseToggle.IsChecked != true || session is null ||
            !InkSurface.Strokes.Contains(gesture) || !ScribbleGesture.IsScribble(gesture)) return false;
        var path = gesture.StylusPoints.Select(p => p.ToPoint()).ToArray();
        var bounds = gesture.GetBounds();
        var erased = InkSurface.Strokes.Where(s => s != gesture && s.HitTest(bounds, 60) &&
            ScribbleGesture.CrossesRepeatedly(path, s.GetBounds()) && s.HitTest(path, new EllipseStylusShape(8, 8))).ToList();
        var replacements = new Dictionary<Guid, PageText>();
        foreach (var box in InkSurface.Children.OfType<TextBox>())
        {
            var text = session.Texts.FirstOrDefault(t => t.Id == (Guid)box.Tag);
            if (text is null || !new Rect(text.X, text.Y, box.ActualWidth, box.ActualHeight).IntersectsWith(bounds)) continue;
            var updated = EraseScribbledWords(box, text, path);
            if (updated != text.Text) replacements[text.Id] = text with { Text = updated };
        }
        if (erased.Count == 0 && replacements.Count == 0) return false;
        // Strokes_Changed recorded the completed mark. Keeping that state means one
        // Undo restores both the erased objects and the user's intentional scribble.
        restoring = true;
        try
        {
            InkSurface.Strokes.Remove(gesture);
            foreach (var stroke in erased) InkSurface.Strokes.Remove(stroke);
            for (var i = session.Texts.Count - 1; i >= 0; i--)
                if (replacements.TryGetValue(session.Texts[i].Id, out var replacement))
                {
                    if (string.IsNullOrWhiteSpace(replacement.Text)) session.Texts.RemoveAt(i);
                    else session.Texts[i] = replacement;
                }
            if (replacements.Count > 0) RenderTexts();
        }
        finally { restoring = false; }
        SaveFullPage();
        EditNotice.Visibility = Visibility.Visible;
        if (!editNoticeInitialized)
        {
            editNoticeInitialized = true;
            editNoticeTimer.Tick += (_, _) => { editNoticeTimer.Stop(); EditNotice.Visibility = Visibility.Collapsed; };
        }
        editNoticeTimer.Stop(); editNoticeTimer.Start();
        return true;
    }
    private string EraseScribbledWords(TextBox box, PageText text, IReadOnlyList<Point> path)
    {
        var remove = new bool[text.Text.Length];
        for (var start = 0; start < text.Text.Length;)
        {
            if (char.IsWhiteSpace(text.Text[start])) { start++; continue; }
            var end = start;
            while (end < text.Text.Length && !char.IsWhiteSpace(text.Text[end])) end++;
            var hit = false;
            for (var i = start; i < end && !hit; i++)
            {
                var leading = box.GetRectFromCharacterIndex(i);
                var trailing = box.GetRectFromCharacterIndex(i, trailingEdge: true);
                if (leading.IsEmpty || trailing.IsEmpty) continue;
                var origin = box.TranslatePoint(leading.TopLeft, InkSurface);
                var character = new Rect(origin, new Size(Math.Max(1, trailing.X - leading.X), leading.Height));
                hit = ScribbleGesture.CrossesRepeatedly(path, character);
            }
            if (hit)
            {
                var through = end;
                while (through < text.Text.Length && text.Text[through] is ' ' or '\t') through++;
                for (var i = start; i < through; i++) remove[i] = true;
            }
            start = end;
        }
        var result = new StringBuilder(text.Text.Length);
        for (var i = 0; i < text.Text.Length; i++) if (!remove[i]) result.Append(text.Text[i]);
        return result.ToString();
    }
}
