using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

/// <summary>Where RWG seeds each biome before growing them (WorldBuilder.BiomeLayout).</summary>
public enum BiomeLayoutKind
{
    /// <summary>Forest in the middle, the others in a ring around it.</summary>
    CenterForest,
    /// <summary>Wasteland in the middle, the others in a ring around it.</summary>
    CenterWasteland,
    /// <summary>Every biome on one ring.</summary>
    Circle,
    /// <summary>Each biome twice, on an outer and an inner ring.</summary>
    Circle2,
    /// <summary>Every biome along a line across the middle.</summary>
    Line,
}

/// <summary>A biome and its share of the map (a weight; the shares are compared with each other, not percentages).</summary>
public sealed record BiomeShare(string Name, uint Color, int Weight);

/// <summary>A generated biome map: the layout and each biome's share.</summary>
public sealed record BiomePlan(BiomeLayoutKind Layout, IReadOnlyList<BiomeShare> Shares);

/// <summary>
/// RWG's biome layout: the map is cut into 256 m tiles, each biome gets a share of them and a seed tile placed by the
/// layout, then biomes grow ring by ring into empty tiles next to their own. The tiles are painted into biomes.png with
/// the game's filler_biome stamp so the edges look natural.
/// </summary>
public static class BiomeLayout
{
    public const int TileSize = 256;

    public const uint ForestColor = 0x004000, BurntColor = 0xBA00FF, DesertColor = 0xFFE477, SnowColor = 0xFFFFFF, WastelandColor = 0xFFA800;

    /// <summary>RWG's default weights (WorldBuilder's *BiomeWeight). Other (mod) biomes start at 0.</summary>
    public static int DefaultWeight(uint color) => color switch
    {
        ForestColor => 13,
        BurntColor => 18,
        DesertColor => 22,
        SnowColor => 23,
        WastelandColor => 24,
        _ => 0,
    };

    /// <summary>One share per land biome (not underwater), at RWG's default weights.</summary>
    public static List<BiomeShare> DefaultShares(BiomeTable biomes) =>
        biomes.Biomes.Where(b => !b.Name.Contains("underwater", StringComparison.OrdinalIgnoreCase))
            .Select(b => new BiomeShare(b.Name, b.Color, DefaultWeight(b.Color))).ToList();

    /// <summary>A seed biome's tile quota and seed tile while the layout grows.</summary>
    private sealed class Seed(int biome, float percent, int tiles)
    {
        public int Biome { get; } = biome;
        public float Percent { get; } = percent;
        public int Tiles { get; set; } = tiles;
        public int X { get; set; }
        public int Z { get; set; }
    }

    /// <summary>
    /// Plans the 256 m tiles: each holds an index into <paramref name="shares"/>. Biomes with weight 0 get none.
    /// </summary>
    public static int[,] PlanTiles(int worldSize, IReadOnlyList<BiomeShare> shares, BiomeLayoutKind layout, Random rand)
    {
        int w = worldSize / TileSize;
        var tiles = new int[w, w];
        for (int x = 0; x < w; x++)
            for (int z = 0; z < w; z++)
                tiles[x, z] = -1;

        int fallback = Fallback(shares);
        if (fallback < 0) throw new ArgumentException("At least one biome needs a share above 0.", nameof(shares));

        int copies = layout == BiomeLayoutKind.Circle2 ? 2 : 1;
        var seeds = Seeds(shares, w * w, copies);
        uint? centreColor = layout switch
        {
            BiomeLayoutKind.CenterForest => ForestColor,
            BiomeLayoutKind.CenterWasteland => WastelandColor,
            _ => null,
        };
        bool IsCentre(Seed s) => centreColor is { } c && shares[s.Biome].Color == c;

        // Seed tiles.
        float mid = w * 0.5f, radius = w * 0.4f;
        if (layout == BiomeLayoutKind.Line)
        {
            double a = rand.Next(4) * Math.PI / 2;
            float dx = (float)Math.Cos(a) * radius, dz = (float)Math.Sin(a) * radius;
            for (int i = 0; i < seeds.Count; i++)
            {
                float t = seeds.Count == 1 ? 0 : i / (float)(seeds.Count - 1);
                Plant(seeds[i], (int)(mid - dx + 2 * dx * t), (int)(mid - dz + 2 * dz * t));
            }
        }
        else
        {
            // The centre biome sits in the middle, the rest on a ring (Circle2: the second half on an inner ring).
            int split = layout == BiomeLayoutKind.Circle2 ? seeds.Count / 2 : -1;
            int slots = layout switch
            {
                BiomeLayoutKind.Circle => seeds.Count,
                BiomeLayoutKind.Circle2 => split,
                _ => seeds.Count - 1,
            };
            double angle = rand.NextDouble() * 360;
            double step = 360.0 / Math.Max(1, slots);
            if (rand.NextDouble() < 0.5) step = -step;
            for (int i = 0; i < seeds.Count; i++)
            {
                if (IsCentre(seeds[i]))
                {
                    Plant(seeds[i], (int)mid, (int)mid);
                    continue;
                }
                if (i == split)
                {
                    angle += rand.Next(70, 200);
                    radius *= 0.5f;
                }
                double rad = angle * Math.PI / 180;
                Plant(seeds[i], (int)(mid + Math.Cos(rad) * radius), (int)(mid + Math.Sin(rad) * radius));
                angle += step;
            }
        }

        void Plant(Seed s, int x, int z)
        {
            s.X = Math.Clamp(x, 0, w - 1);
            s.Z = Math.Clamp(z, 0, w - 1);
            s.Tiles--;
            tiles[s.X, s.Z] = s.Biome;
        }

        // Grow each biome to its quota, a few tiles per round (more for bigger shares). The centre biome keeps off the
        // map edge, as RWG's does.
        int centreEdge = 1 + worldSize / 2048;
        bool grew;
        do
        {
            grew = false;
            foreach (var s in seeds)
            {
                if (s.Tiles <= 0) continue;
                int edge = IsCentre(s) ? centreEdge : 0;
                int tries = 1 + (int)(s.Percent * 4);
                for (int k = 0; k < tries && s.Tiles > 0 && Grow(tiles, w, s, edge, rand); k++)
                {
                    s.Tiles--;
                    grew = true;
                }
            }
        } while (grew);

        // Then every biome but the fallback takes whatever empty tiles it can still reach; the rest become the fallback.
        do
        {
            grew = false;
            foreach (var s in seeds)
                if (s.Biome != fallback && Grow(tiles, w, s, 0, rand)) grew = true;
        } while (grew);

        for (int x = 0; x < w; x++)
            for (int z = 0; z < w; z++)
                if (tiles[x, z] < 0) tiles[x, z] = fallback;
        return tiles;
    }

    /// <summary>The biome that fills what nothing else reaches: forest, as in RWG, or the biggest share without forest.</summary>
    public static int Fallback(IReadOnlyList<BiomeShare> shares)
    {
        int best = -1;
        for (int i = 0; i < shares.Count; i++)
        {
            if (shares[i].Weight <= 0) continue;
            if (shares[i].Color == ForestColor) return i;
            if (best < 0 || shares[i].Weight > shares[best].Weight) best = i;
        }
        return best;
    }

    /// <summary>Each biome's share and tile quota, biggest first; leftover tiles go round the list one at a time.</summary>
    private static List<Seed> Seeds(IReadOnlyList<BiomeShare> shares, int totalTiles, int copies)
    {
        float total = shares.Sum(s => Math.Max(0, s.Weight)) * (float)copies;
        var seeds = new List<Seed>();
        for (int c = 0; c < copies; c++)
        {
            seeds.AddRange(shares.Select((s, i) => (s, i)).Where(p => p.s.Weight > 0)
                .Select(p =>
                {
                    float percent = p.s.Weight / total;
                    return new Seed(p.i, percent, Math.Max(1, (int)MathF.Floor(percent * totalTiles)));
                })
                .OrderByDescending(s => s.Percent));
        }
        int given = seeds.Sum(s => s.Tiles);
        for (int i = 0; given < totalTiles; given++, i = (i + 1) % seeds.Count) seeds[i].Tiles++;
        return seeds;
    }

    /// <summary>
    /// Claims the nearest empty tile beside one of the biome's own, searching outwards from its seed (jittered by up to
    /// 2 tiles) in growing square rings. False if none is in reach.
    /// </summary>
    private static bool Grow(int[,] tiles, int w, Seed s, int edge, Random rand)
    {
        int max = w - 1 - edge;
        int cx = s.X + rand.Next(-2, 3), cz = s.Z + rand.Next(-2, 3);
        for (int r = 1; r < 40; r++)
        {
            for (int d = 0; d <= r; d++)
            {
                if (TryClaim(cx - d, cz - r) || TryClaim(cx + d, cz - r) || TryClaim(cx - d, cz + r) || TryClaim(cx + d, cz + r)
                    || TryClaim(cx - r, cz - d) || TryClaim(cx - r, cz + d) || TryClaim(cx + r, cz - d) || TryClaim(cx + r, cz + d))
                    return true;
            }
        }
        return false;

        bool TryClaim(int x, int z)
        {
            if (x < edge || x > max || z < edge || z > max || tiles[x, z] >= 0) return false;
            bool beside = x > 0 && tiles[x - 1, z] == s.Biome || x < w - 1 && tiles[x + 1, z] == s.Biome
                          || z > 0 && tiles[x, z - 1] == s.Biome || z < w - 1 && tiles[x, z + 1] == s.Biome;
            if (!beside) return false;
            tiles[x, z] = s.Biome;
            return true;
        }
    }

    /// <summary>
    /// Paints planned tiles into a biome layer of <paramref name="worldSize"/>/8 cells. Each tile presses the filler
    /// stamp's shape in its biome (randomly scaled and turned) over a fallback-biome background, so neighbours bite into
    /// each other. Without the stamp, tiles are painted as squares.
    /// </summary>
    public static IndexedLayer Paint(int worldSize, IReadOnlyList<BiomeShare> shares, int[,] tiles, StampImage? filler, Random rand)
    {
        int cells = worldSize / 8, perTile = TileSize / 8, w = worldSize / TileSize;
        // Palette: the biomes the plan uses, in share order.
        var used = Enumerable.Range(0, shares.Count).Where(i => tiles.Cast<int>().Contains(i)).ToList();
        var paletteIndex = new byte[shares.Count];
        for (int i = 0; i < used.Count; i++) paletteIndex[used[i]] = (byte)i;
        var grid = new Grid<byte>(cells, cells);
        Array.Fill(grid.Data, paletteIndex[Fallback(shares)]);

        for (int tz = 0; tz < w; tz++)
        {
            for (int tx = 0; tx < w; tx++)
            {
                byte value = paletteIndex[tiles[tx, tz]];
                if (filler is null)
                {
                    for (int z = tz * perTile; z < (tz + 1) * perTile; z++)
                        for (int x = tx * perTile; x < (tx + 1) * perTile; x++)
                            grid[x, z] = value;
                    continue;
                }
                float scale = perTile / (float)filler.Size * 1.5f + (float)rand.NextDouble() * 0.2f;
                float angle = rand.Next(4) * 90 + rand.Next(-20, 20);
                PressShape(grid, filler, tx * perTile + perTile / 2f, tz * perTile + perTile / 2f, scale, angle, value);
            }
        }
        return new IndexedLayer(grid, used.Select(i => shares[i].Color).ToList());
    }

    /// <summary>Sets every cell where the stamp's alpha, scaled and turned about (cx, cz), is above 0.1.</summary>
    private static void PressShape(Grid<byte> grid, StampImage stamp, float cx, float cz, float scale, float degrees, byte value)
    {
        float half = stamp.Size * scale * 0.7072f; // half the diagonal, so any turn fits
        double rad = degrees * Math.PI / 180;
        float cos = (float)Math.Cos(rad), sin = (float)Math.Sin(rad);
        int x0 = Math.Max(0, (int)(cx - half)), x1 = Math.Min(grid.Width - 1, (int)(cx + half));
        int z0 = Math.Max(0, (int)(cz - half)), z1 = Math.Min(grid.Height - 1, (int)(cz + half));
        float mid = stamp.Size / 2f;
        for (int z = z0; z <= z1; z++)
        {
            float dz = (z + 0.5f - cz) / scale;
            for (int x = x0; x <= x1; x++)
            {
                float dx = (x + 0.5f - cx) / scale;
                // Turn the cell back into the stamp's own frame.
                float u = dx * cos + dz * sin + mid, v = -dx * sin + dz * cos + mid;
                if (stamp.SampleAlpha(u, v) > 0.1f) grid[x, z] = value;
            }
        }
    }
}
