using System.Windows.Ink;

namespace SurfaceNotebook.Services;

// History collections share private immutable stroke copies. Appending a stroke
// copies its points once, rather than cloning every previous stroke after each edit.
// Undo/redo returns independent editable clones. Existing ink is not transformed
// by the v0.2 editor; pan/zoom only changes the viewport.
public sealed class InkHistory
{
    private const int Capacity = 30;
    private readonly List<StrokeCollection> states = [new()];
    private readonly Dictionary<Guid, Stroke> copies = [];
    private int position;
    public bool CanUndo => position > 0;
    public bool CanRedo => position < states.Count - 1;

    public void Record(StrokeCollection strokes)
    {
        states.RemoveRange(position + 1, states.Count - position - 1);
        states.Add(Snapshot(strokes));
        if (states.Count > Capacity) states.RemoveAt(0);
        position = states.Count - 1;
    }

    public StrokeCollection Undo() => states[CanUndo ? --position : position].Clone();
    public StrokeCollection Redo() => states[CanRedo ? ++position : position].Clone();

    public void Reset(StrokeCollection strokes)
    {
        states.Clear();
        copies.Clear();
        states.Add(Snapshot(strokes));
        position = 0;
    }

    private StrokeCollection Snapshot(StrokeCollection strokes)
    {
        var snapshot = new StrokeCollection();
        foreach (var stroke in strokes)
        {
            var id = InkMapper.Identity(stroke);
            if (!copies.TryGetValue(id, out var copy)) copies[id] = copy = stroke.Clone();
            snapshot.Add(copy);
        }
        return snapshot;
    }
}
