using System.Windows.Media.Imaging;

namespace MapEditor.App.Map;

/// <summary>Identifies one tile. <see cref="Generation"/> changes whenever the visible layers change.</summary>
public readonly record struct TileKey(int Level, int X, int Y, int Generation);

/// <summary>
/// A small thread-safe LRU of rendered, frozen tile bitmaps. Each entry remembers the tile's edit epoch
/// when it was rendered, so a tile whose area was edited can be shown (stale) until its replacement arrives.
/// </summary>
public sealed class TileCache(int capacity)
{
    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, BitmapSource Bitmap, int Epoch)>> _map = [];
    private readonly LinkedList<(TileKey Key, BitmapSource Bitmap, int Epoch)> _lru = new();
    private readonly object _lock = new();

    public bool TryGet(TileKey key, out BitmapSource bitmap, out int epoch)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                (_, bitmap, epoch) = node.Value;
                return true;
            }
        }
        bitmap = null!;
        epoch = -1;
        return false;
    }

    /// <summary>Stores a tile unless a newer epoch of it is already cached.</summary>
    public void Add(TileKey key, BitmapSource bitmap, int epoch)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing))
            {
                if (existing.Value.Epoch > epoch) return;
                _lru.Remove(existing);
            }
            _map[key] = _lru.AddFirst((key, bitmap, epoch));
            while (_map.Count > capacity)
            {
                var last = _lru.Last!;
                _lru.RemoveLast();
                _map.Remove(last.Value.Key);
            }
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _map.Clear();
            _lru.Clear();
        }
    }
}
