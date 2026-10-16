using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>
/// Converts between the game's PNG masks and editor layers. PNGs are stored north-up (image row 0 =
/// north edge) while layers are south-up, so every conversion flips vertically.
/// </summary>
public static class LayerImages
{
    // splat3 colours written by RWG (PathShared.IdToColor), as RGBA.
    private const uint Splat3None = 0x00000000;
    private const uint Splat3Gravel = 0x00FF00FF;
    private const uint Splat3Asphalt = 0xFF0000FF;
    private const uint Splat3Water = 0x0000FFFF;

    /// <summary>Decodes splat3. <paramref name="canonical"/> is false if it holds colours RWG never writes.</summary>
    public static Grid<byte> DecodeRoads(byte[] rgba, int width, int height, out bool canonical)
    {
        var grid = new Grid<byte>(width, height);
        canonical = true;
        for (int y = 0; y < height; y++)
        {
            var row = grid.Row(height - 1 - y);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                uint c = (uint)(rgba[o] << 24 | rgba[o + 1] << 16 | rgba[o + 2] << 8 | rgba[o + 3]);
                switch (c)
                {
                    case Splat3None: row[x] = (byte)RoadType.None; break;
                    case Splat3Gravel: row[x] = (byte)RoadType.Gravel; break;
                    case Splat3Asphalt: row[x] = (byte)RoadType.Asphalt; break;
                    case Splat3Water: row[x] = (byte)RoadType.Water; break;
                    default:
                        canonical = false;
                        row[x] = rgba[o] > 0 ? (byte)RoadType.Asphalt
                            : rgba[o + 1] > 0 ? (byte)RoadType.Gravel
                            : rgba[o + 2] > 0 ? (byte)RoadType.Water
                            : (byte)RoadType.None;
                        break;
                }
            }
        }
        return grid;
    }

    public static void EncodeRoadsRow(Grid<byte> roads, int y, Span<byte> dst)
    {
        var row = roads.Row(roads.Height - 1 - y);
        for (int x = 0; x < row.Length; x++)
        {
            uint c = (RoadType)row[x] switch
            {
                RoadType.Gravel => Splat3Gravel,
                RoadType.Asphalt => Splat3Asphalt,
                RoadType.Water => Splat3Water,
                _ => Splat3None,
            };
            dst[x * 4] = (byte)(c >> 24);
            dst[x * 4 + 1] = (byte)(c >> 16);
            dst[x * 4 + 2] = (byte)(c >> 8);
            dst[x * 4 + 3] = (byte)c;
        }
    }

    /// <summary>Decodes splat4 (water surface height in B, everything else 0).</summary>
    public static Grid<byte> DecodeWater(byte[] rgba, int width, int height, out bool canonical)
    {
        var grid = new Grid<byte>(width, height);
        canonical = true;
        for (int y = 0; y < height; y++)
        {
            var row = grid.Row(height - 1 - y);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                row[x] = rgba[o + 2];
                if ((rgba[o] | rgba[o + 1] | rgba[o + 3]) != 0) canonical = false;
            }
        }
        return grid;
    }

    public static void EncodeWaterRow(Grid<byte> water, int y, Span<byte> dst)
    {
        var row = water.Row(water.Height - 1 - y);
        dst.Clear();
        for (int x = 0; x < row.Length; x++) dst[x * 4 + 2] = row[x];
    }

    /// <summary>Decodes radiation.png; the game only reads the red channel.</summary>
    public static Grid<byte> DecodeRadiation(byte[] rgba, int width, int height)
    {
        var grid = new Grid<byte>(width, height);
        for (int y = 0; y < height; y++)
        {
            var row = grid.Row(height - 1 - y);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4) row[x] = rgba[o];
        }
        return grid;
    }

    public static void EncodeRadiationRow(Grid<byte> radiation, int y, Span<byte> dst)
    {
        var row = radiation.Row(radiation.Height - 1 - y);
        for (int x = 0; x < row.Length; x++)
        {
            dst[x * 4] = row[x];
            dst[x * 4 + 1] = 0;
            dst[x * 4 + 2] = 0;
            dst[x * 4 + 3] = 255;
        }
    }

    /// <summary>
    /// Decodes biomes.png into palette indices. Returns null if the image has more than 255 distinct
    /// colours (an unusual hand-made map), in which case the caller keeps the raw pixels.
    /// </summary>
    public static IndexedLayer? DecodeBiomes(byte[] rgba, int width, int height)
    {
        var grid = new Grid<byte>(width, height);
        var palette = new List<uint>();
        var lookup = new Dictionary<uint, byte>();
        for (int y = 0; y < height; y++)
        {
            var row = grid.Row(height - 1 - y);
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                uint rgb = (uint)(rgba[o] << 16 | rgba[o + 1] << 8 | rgba[o + 2]);
                if (!lookup.TryGetValue(rgb, out byte index))
                {
                    if (palette.Count == 255) return null;
                    index = (byte)palette.Count;
                    palette.Add(rgb);
                    lookup[rgb] = index;
                }
                row[x] = index;
            }
        }
        return new IndexedLayer(grid, palette);
    }

    public static void EncodeBiomesRow(IndexedLayer biomes, int y, Span<byte> dst)
    {
        var row = biomes.Indices.Row(biomes.Indices.Height - 1 - y);
        for (int x = 0; x < row.Length; x++)
        {
            uint rgb = biomes.Palette[row[x]];
            dst[x * 4] = (byte)(rgb >> 16);
            dst[x * 4 + 1] = (byte)(rgb >> 8);
            dst[x * 4 + 2] = (byte)rgb;
            dst[x * 4 + 3] = 255;
        }
    }
}
