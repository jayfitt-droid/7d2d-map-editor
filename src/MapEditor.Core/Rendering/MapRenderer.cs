using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Rendering;

[Flags]
public enum MapLayers
{
    None = 0,
    Hillshade = 1,
    Biomes = 2,
    Water = 4,
    Roads = 8,
    Radiation = 16,
    All = Hillshade | Biomes | Water | Roads | Radiation,
}

/// <summary>
/// Renders the raster layers of a <see cref="World"/> into square tiles of a mip pyramid, north-up,
/// as premultiplied BGRA32 (alpha is 0 outside the world). Level 0 is one world metre per pixel;
/// each level up halves the resolution. Safe to call from several threads at once.
/// </summary>
public sealed class MapRenderer
{
    public const int TileSize = 256;

    private readonly World _world;
    private readonly uint[] _biomeDisplay = [];
    private readonly int _biomeScale;
    private readonly int _radiationScale;

    // Muted display colours by biome name; biomes.png's own colours are id codes, not meant for viewing.
    private static readonly Dictionary<string, uint> DisplayColours = new(StringComparer.OrdinalIgnoreCase)
    {
        ["pine_forest"] = 0x4F7A3A,
        ["burnt_forest"] = 0x5E5048,
        ["desert"] = 0xD6C08A,
        ["snow"] = 0xE6ECF0,
        ["wasteland"] = 0x8C806A,
        ["underwater"] = 0x2F5C8A,
    };

    public MapRenderer(World world, BiomeTable biomeTable)
    {
        _world = world;
        Levels = LevelCount(world.Size);
        if (world.Biomes is { } b)
        {
            _biomeScale = Math.Max(1, world.Size / b.Indices.Width);
            _biomeDisplay = b.Palette.Select(rgb =>
                biomeTable.FindByColor(rgb) is { } def && DisplayColours.TryGetValue(def.Name, out uint c) ? c : rgb).ToArray();
        }
        if (world.Radiation is { } r) _radiationScale = Math.Max(1, world.Size / r.Width);
    }

    public World World => _world;

    /// <summary>Number of mip levels; the top level fits the whole world in one tile.</summary>
    public int Levels { get; }

    public static int LevelCount(int worldSize)
    {
        int levels = 1;
        while ((TileSize << (levels - 1)) < worldSize) levels++;
        return levels;
    }

    /// <summary>World metres covered by one pixel at <paramref name="level"/>.</summary>
    public static int MetresPerPixel(int level) => 1 << level;

    public int TilesAcross(int level) => (_world.Size + (TileSize << level) - 1) / (TileSize << level);

    /// <summary>
    /// Renders tile (<paramref name="tx"/>, <paramref name="ty"/>) where ty counts down from the north edge.
    /// </summary>
    public void RenderTile(int level, int tx, int ty, MapLayers layers, Span<uint> bgra)
    {
        if (bgra.Length < TileSize * TileSize) throw new ArgumentException("Tile buffer too small.", nameof(bgra));
        int size = _world.Size;
        int step = 1 << level;
        var height = _world.Height;
        bool biomes = (layers & MapLayers.Biomes) != 0 && _world.Biomes is not null;
        bool shade = (layers & MapLayers.Hillshade) != 0;
        var water = (layers & MapLayers.Water) != 0 ? _world.Water : null;
        var roads = (layers & MapLayers.Roads) != 0 ? _world.Roads : null;
        var radiation = (layers & MapLayers.Radiation) != 0 ? _world.Radiation : null;

        for (int py = 0; py < TileSize; py++)
        {
            int v = (ty * TileSize + py) * step;
            var row = bgra.Slice(py * TileSize, TileSize);
            if (v >= size) { row.Clear(); continue; }
            int z = size - 1 - v;

            for (int px = 0; px < TileSize; px++)
            {
                int x = (tx * TileSize + px) * step;
                if (x >= size) { row[px] = 0; continue; }

                float h = height[x, z] / 257f;
                uint rgb = biomes ? _biomeDisplay[_world.Biomes!.Indices[x / _biomeScale, z / _biomeScale]] : ElevationGrey(h);
                float r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;

                if (shade)
                {
                    float light = Hillshade(height, x, z, step, size);
                    r *= light; g *= light; b *= light;
                }

                if (water is not null && water[x, z] is byte surface and > 0 && h <= surface + 0.5f)
                {
                    float depth = Math.Clamp((surface - h) / 12f, 0f, 1f);
                    Blend(ref r, ref g, ref b, 0x3A78B8, 0.55f + 0.35f * depth);
                }

                if (roads is not null && RoadAt(roads, x, z, step, size) is var road and not RoadType.None)
                {
                    uint roadColour = road switch
                    {
                        RoadType.Asphalt => 0x3C3C40u,
                        RoadType.Gravel => 0xB49A6Au,
                        _ => 0x3A78B8u,
                    };
                    Blend(ref r, ref g, ref b, roadColour, 0.9f);
                }

                if (radiation is not null && radiation[x / _radiationScale, z / _radiationScale] > 0)
                    Blend(ref r, ref g, ref b, 0xD02020, 0.45f);

                row[px] = 0xFF000000u | (uint)Math.Min(255f, r) << 16 | (uint)Math.Min(255f, g) << 8 | (uint)Math.Min(255f, b);
            }
        }
    }

    /// <summary>Lambert shading lit from the north-west, returning a brightness multiplier around 1.</summary>
    private static float Hillshade(Grid<ushort> height, int x, int z, int step, int size)
    {
        int x0 = Math.Max(x - step, 0), x1 = Math.Min(x + step, size - 1);
        int z0 = Math.Max(z - step, 0), z1 = Math.Min(z + step, size - 1);
        const float exaggeration = 2f;
        float dx = (height[x1, z] - height[x0, z]) / 257f / Math.Max(1, x1 - x0) * exaggeration;
        float dz = (height[x, z1] - height[x, z0]) / 257f / Math.Max(1, z1 - z0) * exaggeration;
        // Surface normal (-dx, -dz, 1); light from the north-west at 45 degrees.
        const float lx = -0.5f, lz = 0.5f, ly = 0.70710678f;
        float dot = (-dx * lx - dz * lz + ly) / MathF.Sqrt(dx * dx + dz * dz + 1f);
        return 0.35f + 0.9f * Math.Max(0f, dot);
    }

    /// <summary>Road under a pixel; at coarse levels any road in the pixel's block counts, so roads stay visible.</summary>
    private static RoadType RoadAt(Grid<byte> roads, int x, int z, int step, int size)
    {
        if (step == 1) return (RoadType)roads[x, z];
        int zEnd = Math.Max(z - step + 1, 0);
        int xEnd = Math.Min(x + step, size);
        var best = RoadType.None;
        for (int zz = z; zz >= zEnd; zz--)
        {
            var row = roads.Row(zz)[x..xEnd];
            int i = row.IndexOfAnyExcept((byte)0);
            if (i < 0) continue;
            var found = (RoadType)row[i];
            if (found == RoadType.Asphalt) return found;
            if (best == RoadType.None) best = found;
        }
        return best;
    }

    private static uint ElevationGrey(float metres)
    {
        uint g = (uint)Math.Clamp(70 + metres * 0.7f, 0, 255);
        return g << 16 | g << 8 | g;
    }

    private static void Blend(ref float r, ref float g, ref float b, uint rgb, float amount)
    {
        r += (((rgb >> 16) & 0xFF) - r) * amount;
        g += (((rgb >> 8) & 0xFF) - g) * amount;
        b += ((rgb & 0xFF) - b) * amount;
    }
}
