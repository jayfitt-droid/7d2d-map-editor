using MapEditor.Core.Model;

namespace MapEditor.Core.Edit;

/// <summary>A rectangle of grid cells, [X0, X1) x [Z0, Z1).</summary>
public readonly record struct GridRect(int X0, int Z0, int X1, int Z1)
{
    public static readonly GridRect Empty = new(0, 0, 0, 0);

    public bool IsEmpty => X1 <= X0 || Z1 <= Z0;

    public GridRect Union(GridRect other) =>
        IsEmpty ? other : other.IsEmpty ? this
        : new(Math.Min(X0, other.X0), Math.Min(Z0, other.Z0), Math.Max(X1, other.X1), Math.Max(Z1, other.Z1));

    public GridRect Clamp(int width, int height) =>
        new(Math.Max(X0, 0), Math.Max(Z0, 0), Math.Min(X1, width), Math.Min(Z1, height));

    /// <summary>Scales a rectangle from one grid resolution to another (e.g. biome cells to metres).</summary>
    public GridRect Scale(int factor) => new(X0 * factor, Z0 * factor, X1 * factor, Z1 * factor);

    public static GridRect AroundCircle(float cx, float cz, float radius) =>
        new((int)MathF.Floor(cx - radius), (int)MathF.Floor(cz - radius), (int)MathF.Ceiling(cx + radius) + 1, (int)MathF.Ceiling(cz + radius) + 1);
}

/// <summary>
/// Collects "before" copies of the grid tiles one edit (for example a brush stroke) touches, so the
/// edit can be undone. Call <see cref="Touch{T}"/> before changing cells.
/// </summary>
public sealed class EditSession(string name)
{
    internal const int TileSize = 64;
    private readonly Dictionary<(object Grid, int Tile), TileSnapshot> _tiles = [];
    private readonly Dictionary<object, int> _scales = [];

    public string Name { get; } = name;

    /// <summary>Bounds of everything touched, in world metres.</summary>
    public GridRect Bounds { get; private set; } = GridRect.Empty;

    public bool HasChanges => _tiles.Count > 0;

    /// <summary>
    /// Snapshots the tiles of <paramref name="grid"/> overlapping <paramref name="rect"/> (in that grid's
    /// cells) the first time they are touched. <paramref name="metresPerCell"/> converts to world bounds.
    /// </summary>
    public void Touch<T>(Grid<T> grid, GridRect rect, int metresPerCell = 1) where T : unmanaged
    {
        rect = rect.Clamp(grid.Width, grid.Height);
        if (rect.IsEmpty) return;
        _scales[grid] = metresPerCell;
        int tilesAcross = (grid.Width + TileSize - 1) / TileSize;
        for (int tz = rect.Z0 / TileSize; tz <= (rect.Z1 - 1) / TileSize; tz++)
            for (int tx = rect.X0 / TileSize; tx <= (rect.X1 - 1) / TileSize; tx++)
            {
                var key = (grid, tx + tz * tilesAcross);
                if (!_tiles.ContainsKey(key)) _tiles[key] = TileSnapshot<T>.Capture(grid, tx, tz);
            }
        grid.MarkChanged();
        Bounds = Bounds.Union(rect.Scale(metresPerCell));
    }

    public UndoStep ToStep() => new(Name, [.. _tiles.Values], Bounds);
}

/// <summary>One undoable edit. <see cref="Swap"/> exchanges stored and live data, so it serves undo and redo.</summary>
public sealed class UndoStep(string name, List<TileSnapshot> tiles, GridRect bounds)
{
    public string Name { get; } = name;
    public GridRect Bounds { get; } = bounds;
    public long Bytes { get; } = tiles.Sum(t => t.Bytes);

    public void Swap()
    {
        foreach (var tile in tiles) tile.Swap();
    }
}

public abstract class TileSnapshot
{
    public abstract long Bytes { get; }
    public abstract void Swap();
}

internal sealed class TileSnapshot<T> : TileSnapshot where T : unmanaged
{
    private readonly Grid<T> _grid;
    private readonly int _x0, _z0, _w, _h;
    private readonly T[] _data;

    private TileSnapshot(Grid<T> grid, int x0, int z0, int w, int h)
    {
        _grid = grid;
        (_x0, _z0, _w, _h) = (x0, z0, w, h);
        _data = new T[w * h];
    }

    public static TileSnapshot<T> Capture(Grid<T> grid, int tx, int tz)
    {
        int x0 = tx * EditSession.TileSize, z0 = tz * EditSession.TileSize;
        var snap = new TileSnapshot<T>(grid, x0, z0,
            Math.Min(EditSession.TileSize, grid.Width - x0), Math.Min(EditSession.TileSize, grid.Height - z0));
        for (int z = 0; z < snap._h; z++)
            grid.Row(z0 + z).Slice(x0, snap._w).CopyTo(snap._data.AsSpan(z * snap._w, snap._w));
        return snap;
    }

    public override long Bytes => (long)_data.Length * System.Runtime.CompilerServices.Unsafe.SizeOf<T>();

    public override void Swap()
    {
        var row = new T[_w];
        for (int z = 0; z < _h; z++)
        {
            var live = _grid.Row(_z0 + z).Slice(_x0, _w);
            var stored = _data.AsSpan(z * _w, _w);
            live.CopyTo(row);
            stored.CopyTo(live);
            row.CopyTo(stored);
        }
        _grid.MarkChanged();
    }
}

/// <summary>Undo/redo history with a memory budget; the oldest steps are dropped first.</summary>
public sealed class UndoStack(long maxBytes = 1L << 30)
{
    private readonly LinkedList<UndoStep> _undo = new();
    private readonly Stack<UndoStep> _redo = new();
    private long _bytes;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? UndoName => _undo.Last?.Value.Name;
    public string? RedoName => _redo.TryPeek(out var s) ? s.Name : null;

    public event EventHandler? Changed;

    public void Push(EditSession session)
    {
        if (!session.HasChanges) return;
        var step = session.ToStep();
        _undo.AddLast(step);
        _bytes += step.Bytes;
        foreach (var s in _redo) _bytes -= s.Bytes;
        _redo.Clear();
        while (_bytes > maxBytes && _undo.Count > 1)
        {
            _bytes -= _undo.First!.Value.Bytes;
            _undo.RemoveFirst();
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Undoes the latest step and returns it (for its bounds), or null if there is none.</summary>
    public UndoStep? Undo()
    {
        if (_undo.Last is not { } node) return null;
        _undo.RemoveLast();
        node.Value.Swap();
        _redo.Push(node.Value);
        Changed?.Invoke(this, EventArgs.Empty);
        return node.Value;
    }

    public UndoStep? Redo()
    {
        if (!_redo.TryPop(out var step)) return null;
        step.Swap();
        _undo.AddLast(step);
        Changed?.Invoke(this, EventArgs.Empty);
        return step;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _bytes = 0;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
