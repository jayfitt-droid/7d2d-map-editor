namespace MapEditor.Core.Model;

/// <summary>
/// A square-or-rectangular raster in world orientation: row 0 is the south edge (world z = -size/2),
/// column 0 is the west edge. Index = x + z * Width, matching the game's dtm.raw layout.
/// </summary>
public sealed class Grid<T> where T : unmanaged
{
    public Grid(int width, int height)
        : this(width, height, new T[checked(width * height)])
    {
    }

    public Grid(int width, int height, T[] data)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (data.Length != width * height) throw new ArgumentException("Data length does not match dimensions.", nameof(data));
        Width = width;
        Height = height;
        Data = data;
    }

    public int Width { get; }
    public int Height { get; }
    public T[] Data { get; }

    /// <summary>Incremented by editors after any change; 0 means "as loaded".</summary>
    public int Version { get; private set; }

    public void MarkChanged() => Version++;

    /// <summary>A copy of the samples that keeps <see cref="Version"/>, so "unedited since load" survives.</summary>
    public Grid<T> Clone() => new(Width, Height, (T[])Data.Clone()) { Version = Version };

    public T this[int x, int z]
    {
        get => Data[x + z * Width];
        set => Data[x + z * Width] = value;
    }

    public Span<T> Row(int z) => Data.AsSpan(z * Width, Width);

    public bool Contains(int x, int z) => (uint)x < (uint)Width && (uint)z < (uint)Height;
}
