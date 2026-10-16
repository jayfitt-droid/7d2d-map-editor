using System.Windows.Media.Imaging;

namespace MapEditor.App.Map;

/// <summary>Identifies one rendered tile. <see cref="Generation"/> changes whenever the visible layers change.</summary>
public readonly record struct TileKey(int Level, int X, int Y, int Generation);

/// <summary>A small thread-safe LRU of rendered, frozen tile bitmaps.</summary>
public sealed class TileCache(int capacity)
{
    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, BitmapSource Bitmap)>> _map = [];
    private readonly LinkedList<(TileKey Key, BitmapSource Bitmap)> _lru = new();
    private readonly object _lock = new();

    public bool TryGet(TileKey key, out BitmapSource bitmap)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }
        bitmap = null!;
        return false;
    }

    public void Add(TileKey key, BitmapSource bitmap)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var existing)) _lru.Remove(existing);
            var node = _lru.AddFirst((key, bitmap));
            _map[key] = node;
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
