using System.Globalization;
using System.Xml.Linq;

namespace MapEditor.Core.GameData;

/// <summary>A township type from rwgmixer.xml (countrytown, town, city...).</summary>
public sealed record TownshipType(
    string Name,
    IReadOnlyList<string> Biomes,
    bool SpawnTrader,
    bool SpawnGateway,
    string? OutskirtDistrict,
    float OutskirtPercent)
{
    /// <summary>Tile count range RWG uses for this township on the world size section that was read (min, max).</summary>
    public (int Min, int Max) Tiles { get; init; } = (1, 1);

    public bool IsRoadside => Name.EndsWith("roadside", StringComparison.OrdinalIgnoreCase);
    public bool IsWilderness => Name.EndsWith("wilderness", StringComparison.OrdinalIgnoreCase);
    public bool IsBig => Name.Equals("citybig", StringComparison.OrdinalIgnoreCase);

    /// <summary>True if RWG allows this township in <paramref name="biome"/> (no biome list means any).</summary>
    public bool AllowsBiome(string? biome) =>
        Biomes.Count == 0 || biome is not null && Biomes.Contains(biome, StringComparer.OrdinalIgnoreCase);

    public override string ToString() => Name;
}

public enum DistrictKind
{
    None,
    Commercial,
    Downtown,
    Gateway,
    Industrial,
    Residential,
    Rural,
}

/// <summary>A district from rwgmixer.xml, as DistrictPlanner uses it.</summary>
public sealed record DistrictType(
    string Name,
    string PrefabName,
    IReadOnlyList<string> Tags,
    float Weight,
    IReadOnlyList<string> Townships,
    IReadOnlyList<string> AvoidedNeighbours,
    bool SpawnCustomSizePrefabs,
    (byte R, byte G, byte B) PreviewColour)
{
    /// <summary>The kind is read from the end of the name, as District.Init does.</summary>
    public DistrictKind Kind =>
        Name.EndsWith("commercial") ? DistrictKind.Commercial
        : Name.EndsWith("downtown") ? DistrictKind.Downtown
        : Name.EndsWith("gateway") ? DistrictKind.Gateway
        : Name.EndsWith("industrial") ? DistrictKind.Industrial
        : Name.EndsWith("residential") ? DistrictKind.Residential
        : Name.EndsWith("rural") ? DistrictKind.Rural
        : DistrictKind.None;

    /// <summary>Districts are laid out in this order, highest first: downtown, commercial, others, residential.</summary>
    public float SpawnOrder => Kind switch
    {
        DistrictKind.Downtown => 99,
        DistrictKind.Commercial => 1,
        DistrictKind.Residential => -1,
        _ => 0,
    };
}

/// <summary>A prefab_spawn_adjust rule: changes how likely RWG is to pick matching POIs.</summary>
public sealed record SpawnAdjust(string PartialName, IReadOnlyList<string> Tags, IReadOnlyList<string> BiomeTags,
    float Weight, float Bias, int MinCount, int MaxCount);

/// <summary>The town-planning parts of Data/Config/rwgmixer.xml.</summary>
public sealed class RwgMixer
{
    public List<TownshipType> Townships { get; } = [];
    public Dictionary<string, DistrictType> Districts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Per street tile: how many a world may have (min, max) and its POI density budget.</summary>
    public Dictionary<string, (int Min, int Max)> TileCounts { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, float> TileMaxDensity { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SpawnAdjust> SpawnAdjusts { get; } = [];

    public static RwgMixer Empty { get; } = new();

    public static RwgMixer Load(GameInstall install, int worldSize) =>
        Parse(XDocument.Load(Path.Combine(install.Root, "Data", "Config", "rwgmixer.xml")), worldSize);

    public static RwgMixer Parse(XDocument doc, int worldSize)
    {
        var mixer = new RwgMixer();
        var root = doc.Root ?? throw new InvalidDataException("rwgmixer.xml has no root element.");

        // The world section whose world_size range holds this world gives the township tile counts.
        var tiles = new Dictionary<string, (int, int)>(StringComparer.OrdinalIgnoreCase);
        foreach (var world in root.Elements("world"))
        {
            var range = Props(world).GetValueOrDefault("world_size")?.Split(',');
            if (range is not { Length: 2 } || !(Int(range[0]) <= worldSize && worldSize <= Int(range[1]))) continue;
            foreach (var c in world.Elements("property").Where(p => p.Attribute("class") is not null))
                if (Props(c).GetValueOrDefault("tiles")?.Split(',') is { Length: 2 } t)
                    tiles[(string)c.Attribute("class")!] = (Int(t[0]), Int(t[1]));
        }

        foreach (var e in root.Elements())
        {
            var props = Props(e);
            string name = ((string?)e.Attribute("name") ?? "").Trim();
            switch (e.Name.LocalName)
            {
                case "township" when name.Length > 0:
                    var outskirt = props.GetValueOrDefault("outskirt_district")?.Split(',');
                    mixer.Townships.Add(new TownshipType(
                        name,
                        List(props.GetValueOrDefault("biomes")),
                        Bool(props.GetValueOrDefault("spawn_trader"), true),
                        Bool(props.GetValueOrDefault("spawn_gateway"), true),
                        outskirt?[0].Trim(),
                        outskirt is null ? 0 : outskirt.Length >= 2 ? Float(outskirt[1]) : 1)
                    {
                        Tiles = tiles.GetValueOrDefault(name, (1, 1)),
                    });
                    break;
                case "district" when name.Length > 0:
                    string lower = name.ToLowerInvariant();
                    var colour = List(props.GetValueOrDefault("preview_color")).Select(Float).ToArray();
                    mixer.Districts[lower] = new DistrictType(
                        lower,
                        (props.GetValueOrDefault("prefab_name")?.ToLowerInvariant() ?? lower) + "_",
                        props.TryGetValue("tag", out var tag) ? List(tag.ToLowerInvariant()) : [lower],
                        props.TryGetValue("spawn_weight", out var w) ? Float(w) : 0.5f,
                        List(props.GetValueOrDefault("required_township")?.ToLowerInvariant()),
                        List(props.GetValueOrDefault("avoided_neighbor_districts")?.ToLowerInvariant()),
                        Bool(props.GetValueOrDefault("spawn_custom_size_prefabs"), false),
                        colour.Length == 3 ? ((byte)(colour[0] * 255), (byte)(colour[1] * 255), (byte)(colour[2] * 255)) : ((byte)128, (byte)128, (byte)128));
                    break;
                case "streettile" when name.Length > 0:
                    int min = props.TryGetValue("mintiles", out var mn) ? Int(mn) : 0;
                    int max = props.TryGetValue("maxtiles", out var mx) ? Int(mx) : int.MaxValue;
                    if (min > 0 || max != int.MaxValue) mixer.TileCounts[name] = (min, max);
                    if (props.TryGetValue("maxdensity", out var d)) mixer.TileMaxDensity[name] = Int(d);
                    break;
                case "prefab_spawn_adjust":
                    string partial = ((string?)e.Attribute("partial_name") ?? "").ToLowerInvariant();
                    var tags = List((string?)e.Attribute("tags"));
                    if (partial.Length == 0 && tags.Count == 0) break;
                    mixer.SpawnAdjusts.Add(new SpawnAdjust(partial, tags, List((string?)e.Attribute("biomeTags")),
                        e.Attribute("weight") is { } wt ? Float(wt.Value) : 1,
                        e.Attribute("bias") is { } b ? Float(b.Value) : 0,
                        e.Attribute("min_count") is { } mc ? Int(mc.Value) : 1,
                        e.Attribute("max_count") is { } xc ? Int(xc.Value) : int.MaxValue));
                    break;
            }
        }
        return mixer;
    }

    /// <summary>Townships a player can build: not wilderness, and with at least one district (or gateways only).</summary>
    public IEnumerable<TownshipType> BuildableTownships => Townships.Where(t => !t.IsWilderness
        && (t.IsRoadside || Districts.Values.Any(d => d.Weight > 0 && d.Townships.Contains(t.Name, StringComparer.OrdinalIgnoreCase))));

    private static Dictionary<string, string> Props(XElement e) =>
        e.Elements("property")
            .Where(p => p.Attribute("name") is not null && p.Attribute("value") is not null)
            .GroupBy(p => (string)p.Attribute("name")!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (string)g.Last().Attribute("value")!, StringComparer.OrdinalIgnoreCase);

    private static List<string> List(string? s) =>
        s is null ? [] : [.. s.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];

    private static int Int(string s) => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static float Float(string s) => float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;

    private static bool Bool(string? s, bool fallback) => s is null ? fallback : s.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
}
