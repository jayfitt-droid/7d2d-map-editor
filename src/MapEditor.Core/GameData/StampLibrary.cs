using MapEditor.Core.IO;

namespace MapEditor.Core.GameData;

/// <summary>
/// A terrain stamp from Data/Stamps. Channels: height in metres, alpha 0..1 (how strongly the stamp
/// applies), and an optional water mask. Row 0 is the stamp's south edge, as in the game.
/// </summary>
public sealed class StampImage
{
    public required string Name { get; init; }
    public required int Size { get; init; }
    public float[]? Height { get; init; }
    public float HeightConst { get; init; }
    public float[]? Alpha { get; init; }
    public float AlphaConst { get; init; } = 1;
    public float[]? Water { get; init; }

    public bool HasWater => Water is not null;

    /// <summary>Bilinear sample at stamp pixel coords; 0 outside the image.</summary>
    public static float Sample(float[] data, int size, float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        if ((uint)x0 >= (uint)size || (uint)y0 >= (uint)size) return 0;
        int x1 = Math.Min(x0 + 1, size - 1), y1 = Math.Min(y0 + 1, size - 1);
        float fx = x - x0, fy = y - y0;
        float a = data[x0 + y0 * size], b = data[x1 + y0 * size];
        float c = data[x0 + y1 * size], d = data[x1 + y1 * size];
        float top = a + (b - a) * fx, bottom = c + (d - c) * fx;
        return top + (bottom - top) * fy;
    }

    public float SampleAlpha(float x, float y) =>
        Alpha is null ? (x >= 0 && y >= 0 && x < Size && y < Size ? AlphaConst : 0) : Sample(Alpha, Size, x, y);

    public float SampleHeight(float x, float y) => Height is null ? HeightConst : Sample(Height, Size, x, y);

    public float SampleWater(float x, float y) => Water is null ? 0 : Sample(Water, Size, x, y);
}

/// <summary>Stamps available in a game install. Images load on first use and are cached.</summary>
public sealed class StampLibrary
{
    private readonly Dictionary<string, string> _files;
    private readonly Dictionary<string, StampImage> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    private StampLibrary(Dictionary<string, string> files) => _files = files;

    public static StampLibrary Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

    /// <summary>Stamp names usable for terrain (the rwg_tile_* road masks and biome filler are excluded).</summary>
    public IReadOnlyList<string> Names => _files.Keys
        .Where(n => !n.StartsWith("rwg_tile", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("filler_biome", StringComparison.OrdinalIgnoreCase))
        .Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static StampLibrary Load(GameInstall install) => Load(install.StampsDir);

    public static StampLibrary Load(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".raw" or ".png")) continue;
                // Like the game, a .raw stamp wins over a .png of the same name.
                string name = Path.GetFileNameWithoutExtension(file);
                if (ext == ".raw" || !files.ContainsKey(name)) files[name] = file;
            }
        }
        return new StampLibrary(files);
    }

    /// <summary>All stamps whose name starts with <paramref name="prefix"/> (the game picks among these at random).</summary>
    public IReadOnlyList<string> WithPrefix(string prefix) =>
        Names.Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToList();

    public StampImage Get(string name)
    {
        lock (_lock)
        {
            if (_loaded.TryGetValue(name, out var stamp)) return stamp;
            if (!_files.TryGetValue(name, out var file)) throw new KeyNotFoundException($"No stamp named '{name}'.");
            stamp = Path.GetExtension(file).Equals(".raw", StringComparison.OrdinalIgnoreCase) ? ReadRaw(name, file) : ReadPng(name, file);
            _loaded[name] = stamp;
            return stamp;
        }
    }

    /// <summary>The game's .raw stamp: per pixel three little-endian uint16s (height, alpha, water), rows from the south.</summary>
    internal static StampImage ReadRaw(string name, string file)
    {
        var bytes = File.ReadAllBytes(file);
        int count = bytes.Length / 6;
        int size = (int)Math.Sqrt(count);
        if (size * size != count) throw new InvalidDataException($"Stamp {name} is not square.");
        var r = new float[count];
        var a = new float[count];
        var b = new float[count];
        for (int i = 0, o = 0; i < count; i++, o += 6)
        {
            r[i] = (bytes[o] | bytes[o + 1] << 8) / 65535f * 255f;
            a[i] = (bytes[o + 2] | bytes[o + 3] << 8) / 65535f;
            b[i] = (bytes[o + 4] | bytes[o + 5] << 8) / 65535f * 255f;
        }
        return Build(name, size, r, a, b);
    }

    /// <summary>A PNG stamp (rivers): R = height, A = alpha, B = water; the image is north-up so rows are flipped.</summary>
    internal static StampImage ReadPng(string name, string file)
    {
        var rgba = Png.ReadRgba(file, out int w, out int h);
        if (w != h) throw new InvalidDataException($"Stamp {name} is not square.");
        var r = new float[w * h];
        var a = new float[w * h];
        var b = new float[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int o = (y * w + x) * 4, i = x + (h - 1 - y) * w;
                r[i] = rgba[o];
                b[i] = rgba[o + 2];
                a[i] = rgba[o + 3] / 255f;
            }
        return Build(name, w, r, a, b);
    }

    /// <summary>Mirrors the game's loader: constant channels become constants, an all-zero water mask is dropped.</summary>
    private static StampImage Build(string name, int size, float[] r, float[] a, float[] b)
    {
        bool heightVaries = r.AsSpan().ContainsAnyExcept(r[0]);
        bool alphaVaries = a.AsSpan().ContainsAnyExcept(a[0]);
        bool hasWater = !name.Contains("rwg_tile", StringComparison.OrdinalIgnoreCase) && b.AsSpan().ContainsAnyExcept(0f);
        return new StampImage
        {
            Name = name,
            Size = size,
            Height = heightVaries ? r : null,
            HeightConst = r[0],
            Alpha = alphaVaries ? a : null,
            AlphaConst = a[0],
            Water = hasWater ? b : null,
        };
    }
}
