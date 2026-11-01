using System.Windows.Ink;
using SurfaceNotebook.Models;

namespace SurfaceNotebook.Services;

// History collections share private immutable stroke copies. Appending a stroke
// copies its points once, rather than cloning every previous stroke after each edit.
// Undo/redo returns independent editable clones. Existing ink is not transformed
// by the v0.2 editor; pan/zoom only changes the viewport.
public sealed class InkHistory
{
    private const int Capacity = 30;
    private sealed record State(StrokeCollection Strokes, List<PageText> Texts, List<PageImage> Images);
    private readonly List<State> states = [new(new(), [], [])];
    private readonly Dictionary<Guid, Stroke> copies = [];
    private int position;
    public bool CanUndo => position > 0;
    public bool CanRedo => position < states.Count - 1;
    public List<PageImage> Images => [.. states[position].Images];
    public List<PageText> Texts => [.. states[position].Texts];

    public void Record(StrokeCollection strokes, IEnumerable<PageText>? texts = null, IEnumerable<PageImage>? images = null)
    {
        states.RemoveRange(position + 1, states.Count - position - 1);
        states.Add(new State(Snapshot(strokes), texts?.ToList() ?? [], images?.ToList() ?? []));
        if (states.Count > Capacity) states.RemoveAt(0);
        position = states.Count - 1;
    }

    public StrokeCollection Undo() => states[CanUndo ? --position : position].Strokes.Clone();
    public StrokeCollection Redo() => states[CanRedo ? ++position : position].Strokes.Clone();

    public void Reset(StrokeCollection strokes, IEnumerable<PageText>? texts = null, IEnumerable<PageImage>? images = null)
    {
        states.Clear();
        copies.Clear();
        states.Add(new State(Snapshot(strokes), texts?.ToList() ?? [], images?.ToList() ?? []));
        position = 0;
    }

    private static Stroke CopyContent(Stroke stroke)
    {
        // WPF Stroke.Clone() also copies its internal IsSelected flag. A history
        // snapshot must contain document content only, never lasso rendering state.
        var clone = stroke.Clone();
        var copy = new Stroke(clone.StylusPoints, clone.DrawingAttributes);
        foreach (var id in clone.GetPropertyDataIds()) copy.AddPropertyData(id, clone.GetPropertyData(id));
        return copy;
    }

    private StrokeCollection Snapshot(StrokeCollection strokes)
    {
        var snapshot = new StrokeCollection();
        foreach (var stroke in strokes)
        {
            var id = InkMapper.Identity(stroke);
            if (!copies.TryGetValue(id, out var copy)) copies[id] = copy = CopyContent(stroke);
            snapshot.Add(copy);
        }
        return snapshot;
    }
}
