using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Ops;

public enum EdgeStyle
{
    /// <summary>Leave the edge as is.</summary>
    None,
    /// <summary>Raised rough land along the edge (the game's land_border stamps).</summary>
    Land,
    /// <summary>Ocean along the edge (the game's water_border stamps, filled with water).</summary>
    Water,
}

/// <param name="Size">World edge in metres; a multiple of 512.</param>
/// <param name="BaseHeight">Starting ground height in metres (RWG uses 35; water sits at 30).</param>
/// <param name="NoiseAmplitude">Peak height variation from noise, in metres (0 for flat).</param>
/// <param name="NoiseScale">Size of noise features in metres.</param>
/// <param name="Biome">biomes.png colour for the whole map, when there is no <paramref name="BiomePlan"/>.</param>
/// <param name="Edges">North, east, south, west.</param>
/// <param name="BiomePlan">A generated layout of several biomes (RWG's method), from the same seed.</param>
public sealed record NewWorldOptions(
    string Name,
    int Size,
    float BaseHeight = 40,
    float NoiseAmplitude = 12,
    float NoiseScale = 600,
    int Seed = 1,
    uint Biome = 0x004000,
    EdgeStyle[]? Edges = null,
    BiomePlan? BiomePlan = null);

/// <summary>Creates a blank world ready for editing.</summary>
public static class NewWorldGenerator
{
    public const int MinSize = 1024;
    public const int MaxSize = 16384;

    public static World Create(NewWorldOptions o, StampLibrary stamps, byte[]? levelTemplate)
    {
        if (o.Size % 512 != 0 || o.Size < MinSize || o.Size > MaxSize)
            throw new ArgumentOutOfRangeException(nameof(o), $"World size must be a multiple of 512 between {MinSize} and {MaxSize}.");

        int size = o.Size;
        var world = new World { Name = o.Name, Size = size, Height = new Grid<ushort>(size, size) };

        var noise = o.NoiseAmplitude > 0 ? new Noise(o.Seed) : null;
        Parallel.For(0, size, z =>
        {
            var row = world.Height.Row(z);
            for (int x = 0; x < size; x++)
            {
                float h = o.BaseHeight;
                if (noise is not null) h += o.NoiseAmplitude * noise.Fbm(x / o.NoiseScale, z / o.NoiseScale);
                row[x] = TerrainBrush.ToRaw(h);
            }
        });

        world.Biomes = o.BiomePlan is { } plan
            ? GenerateBiomes(size, plan, stamps, o.Seed)
            : new IndexedLayer(new Grid<byte>(size / 8, size / 8), [o.Biome]);
        world.Roads = new Grid<byte>(size, size);
        world.Water = new Grid<byte>(size, size);
        world.Radiation = new Grid<byte>(size / 32, size / 32);

        var edges = o.Edges ?? [EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land];
        AddBorders(world, edges, stamps, new Random(o.Seed));

        world.MapInfo = new XDocument(new XElement("MapInfo",
            Property("Scale", "1"),
            Property("HeightMapSize", $"{size},{size}"),
            Property("Modes", "Survival,SurvivalSP,SurvivalMP,Creative"),
            Property("FixedWaterLevel", "false"),
            Property("RandomGeneratedWorld", "true")));
        // No GameVersion: the game only warns about version mismatches when the property is present.

        if (levelTemplate is not null)
            world.ExtraFiles[IO.WorldFiles.Level] = IO.LevelFile.WithNewGuid(levelTemplate);
        return world;
    }

    /// <summary>Plans and paints a biome layout; the same seed gives the same map.</summary>
    public static IndexedLayer GenerateBiomes(int size, BiomePlan plan, StampLibrary stamps, int seed)
    {
        // Separate streams from the terrain noise and borders, as RWG uses its own for the biome paint.
        var tiles = BiomeLayout.PlanTiles(size, plan.Shares, plan.Layout, new Random(seed + 1));
        var paint = new Random(seed + 3);
        var fillers = stamps.WithPrefix("filler_biome");
        var filler = fillers.Count == 0 ? null : stamps.Get(fillers[paint.Next(fillers.Count)]);
        return BiomeLayout.Paint(size, plan.Shares, tiles, filler, paint);
    }

    /// <summary>
    /// Stamps the game's border stamps along each edge every 160 m, as RWG's GenerateBaseStamps does:
    /// land borders sit just inside the edge, water borders 40 m outside it with their water mask filled.
    /// </summary>
    public static void AddBorders(World world, EdgeStyle[] edges, StampLibrary stamps, Random rand)
    {
        var land = stamps.WithPrefix("land_border");
        var water = stamps.WithPrefix("water_border");
        int size = world.Size;
        var session = new EditSession("Borders");

        for (int along = 0; along < size + 160; along += 160)
        {
            for (int edge = 0; edge < 4; edge++)
            {
                var style = edges[edge];
                var names = style == EdgeStyle.Water ? water : style == EdgeStyle.Land ? land : [];
                if (names.Count == 0) continue;
                var stamp = stamps.Get(names[rand.Next(names.Count)]);

                float inset = style == EdgeStyle.Water ? -40 : rand.Next(0, 75);
                float jitter = style == EdgeStyle.Water ? 0 : rand.Next(0, 75);
                (float x, float z) = edge switch
                {
                    0 => (along + jitter, size - inset), // north
                    1 => (size - inset, along + jitter), // east
                    2 => (along + jitter, inset),        // south
                    _ => (inset, along + jitter),        // west
                };
                float scale = 0.6f + (float)rand.NextDouble() * 0.25f;
                StampOp.Apply(world, stamp, new StampPlacement(x, z, stamp.Size * scale, rand.Next(360),
                    ApplyWater: style == EdgeStyle.Water), session);
            }
        }
    }

    private static XElement Property(string name, string value) =>
        new("property", new XAttribute("name", name), new XAttribute("value", value));
}
