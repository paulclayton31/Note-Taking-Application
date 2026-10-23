using System.Windows.Ink;

namespace SurfaceNotebook.Services;

// Bounded snapshots are sufficient for the single-page prototype.
// Larger notebooks should use edit commands and a memory budget instead.
public sealed class InkHistory
{
    private const int Capacity = 30;
    private readonly List<StrokeCollection> states = [new()];
    private int position;
    public bool CanUndo => position > 0;
    public bool CanRedo => position < states.Count - 1;

    public void Record(StrokeCollection strokes)
    {
        states.RemoveRange(position + 1, states.Count - position - 1);
        states.Add(strokes.Clone());
        if (states.Count > Capacity) states.RemoveAt(0);
        position = states.Count - 1;
    }

    public StrokeCollection Undo() => states[CanUndo ? --position : position].Clone();
    public StrokeCollection Redo() => states[CanRedo ? ++position : position].Clone();

    public void Reset(StrokeCollection strokes)
    {
        states.Clear();
        states.Add(strokes.Clone());
        position = 0;
    }
}
