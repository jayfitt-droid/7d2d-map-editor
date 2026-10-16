using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameData;

public enum MarkerType
{
    None,
    /// <summary>A lot RWG fills with a POI (street tiles and some large POIs).</summary>
    POISpawn,
    /// <summary>A spot for a small part prefab (kiosks, traffic lights, cars...).</summary>
    PartSpawn,
    RoadExit,
}

/// <summary>A POI marker from a prefab's sidecar, in prefab-local block coords (x east, z north, unrotated).</summary>
public sealed record PrefabMarker(
    Int3 Start,
    Int3 Size,
    MarkerType Type,
    string Group = "",
    string Tags = "",
    int Rotations = 0,
    float PartChance = 1,
    string? Part = null)
{
    /// <summary>
    /// This marker after one quarter turn of a prefab of size <paramref name="prefabSize"/>, the same way the
    /// game turns markers with the prefab: counter-clockwise seen from above, (x, z) to (size.z - z, x).
    /// </summary>
    public PrefabMarker TurnedOnce(Int3 prefabSize)
    {
        int ax = prefabSize.Z - Start.Z, az = Start.X;
        int bx = prefabSize.Z - (Start.Z + Size.Z), bz = Start.X + Size.X;
        return this with
        {
            Start = new Int3(Math.Min(ax, bx), Start.Y, Math.Min(az, bz)),
            Size = new Int3(Size.Z, Size.Y, Size.X),
        };
    }
}

/// <summary>Metadata from a prefab's .xml sidecar (Data/Prefabs/**/name.xml).</summary>
public sealed record PrefabInfo(
    string Name,
    string Folder,
    Int3 Size,
    int YOffset,
    int RotationToFaceNorth,
    int DifficultyTier,
    IReadOnlyList<string> Tags,
    string? PreviewImagePath)
{
    public IReadOnlyList<string> Zoning { get; init; } = [];

    public IReadOnlyList<PrefabMarker> Markers { get; init; } = [];

    /// <summary>Themes RWG keeps apart (ThemeTags), and how far apart (ThemeRepeatDistance).</summary>
    public IReadOnlyList<string> ThemeTags { get; init; } = [];
    public int ThemeRepeatDistance { get; init; } = 300;

    /// <summary>How far apart RWG keeps two copies of this prefab.</summary>
    public int DuplicateRepeatDistance { get; init; } = 1000;

    /// <summary>RWG's POI budget cost: (TotalVertices + 50000) / 100000, in whole points.</summary>
    public float DensityScore { get; init; }

    /// <summary>The sidecar's TraderArea flag: the prefab is a trader compound.</summary>
    public bool TraderArea { get; init; }

    /// <summary>Trader compounds, where the game's quests start.</summary>
    public bool IsTrader => TraderArea || Name.StartsWith("trader_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Footprint on the ground after rotating by <paramref name="rotation"/> quarter turns.</summary>
    public (int X, int Z) Footprint(int rotation) => (rotation & 1) == 0 ? (Size.X, Size.Z) : (Size.Z, Size.X);

    /// <summary>RWG's 150 m town street tiles (rwg_tile_*).</summary>
    public bool IsStreetTile => Name.StartsWith("rwg_tile_", StringComparison.OrdinalIgnoreCase);

    /// <summary>Small pieces RWG drops onto markers (Data/Prefabs/Parts).</summary>
    public bool IsPart => HasTag("part");

    /// <summary>Prefabs the game only uses for testing or navigation, not for placing in worlds.</summary>
    public bool IsInternal => HasTag("testonly") || HasTag("navonly")
        || Path.GetFileName(Folder).Equals("Test", StringComparison.OrdinalIgnoreCase);

    public bool HasTag(string tag) => Tags.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));

    /// <summary>The district word of a street tile name (rwg_tile_downtown_cap gives "downtown").</summary>
    public string? District => IsStreetTile && Name.Split('_') is { Length: >= 4 } parts ? parts[2] : null;

    /// <summary>The markers after the prefab is placed with <paramref name="rotation"/> quarter turns.</summary>
    public List<PrefabMarker> RotatedMarkers(int rotation)
    {
        var size = Size;
        var result = Markers.ToList();
        for (int r = 0; r < (rotation & 3); r++)
        {
            for (int i = 0; i < result.Count; i++) result[i] = result[i].TurnedOnce(size);
            size = new Int3(size.Z, size.Y, size.X);
        }
        return result;
    }

    /// <summary>
    /// The way the prefab's front faces once placed with <paramref name="rotation"/>: 0 north, 1 west,
    /// 2 south, 3 east. RotationToFaceNorth is the rotation that turns the front north, and each further
    /// quarter turn goes counter-clockwise.
    /// </summary>
    public int FrontDirection(int rotation) => (rotation - RotationToFaceNorth) & 3;
}

/// <summary>All prefabs available to a game install (vanilla Data/Prefabs plus Mods/*/Prefabs).</summary>
public sealed class PrefabLibrary
{
    private readonly Dictionary<string, PrefabInfo> _byName;

    private PrefabLibrary(Dictionary<string, PrefabInfo> byName) => _byName = byName;

    public static PrefabLibrary Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

    /// <summary>A library of the given prefabs, for tests and tools.</summary>
    public static PrefabLibrary Of(IEnumerable<PrefabInfo> prefabs) =>
        new(prefabs.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase));

    public int Count => _byName.Count;

    public IEnumerable<PrefabInfo> All => _byName.Values;

    public PrefabInfo? Find(string name) => _byName.GetValueOrDefault(name);

    public static PrefabLibrary Load(GameInstall install)
    {
        var roots = new List<string> { install.PrefabsDir };
        string mods = Path.Combine(install.Root, "Mods");
        if (Directory.Exists(mods))
            roots.AddRange(Directory.EnumerateDirectories(mods).Select(m => Path.Combine(m, "Prefabs")).Where(Directory.Exists));
        return Load(roots);
    }

    /// <summary>Scans folders for prefab .xml files. Later roots win on duplicate names (mods override vanilla).</summary>
    public static PrefabLibrary Load(IEnumerable<string> roots)
    {
        var result = new Dictionary<string, PrefabInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var found = new ConcurrentBag<PrefabInfo>();
            Parallel.ForEach(Directory.EnumerateFiles(root, "*.xml", SearchOption.AllDirectories), file =>
            {
                // Every prefab has a .tts beside its .xml; this skips unrelated xml files.
                if (!File.Exists(Path.ChangeExtension(file, ".tts"))) return;
                try
                {
                    if (TryRead(file) is { } info) found.Add(info);
                }
                catch (XmlException)
                {
                    // A broken sidecar should not stop the rest of the library loading.
                }
            });
            foreach (var info in found) result[info.Name] = info;
        }
        return new PrefabLibrary(result);
    }

    public static PrefabInfo? TryRead(string file)
    {
        Int3? size = null;
        int yOffset = 0, rotation = 0, tier = 0;
        bool trader = false;
        IReadOnlyList<string> themeTags = [];
        int themeRepeat = 300, duplicateRepeat = 1000;
        float density = 0;
        IReadOnlyList<string> tags = [], zoning = [];
        var props = new Dictionary<string, string>();

        using var reader = XmlReader.Create(file, new XmlReaderSettings { IgnoreComments = true });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "property") continue;
            // Stats.TotalVertices sets the density score; every other property we read sits at the top level.
            if (reader.Depth == 2 && reader.GetAttribute("name") == "TotalVertices" && reader.GetAttribute("value") is { } vertices)
            {
                density = (ParseInt(vertices) + 50000) / 100000;
                continue;
            }
            if (reader.Depth != 1) continue;
            string? value = reader.GetAttribute("value");
            if (value is null || reader.GetAttribute("name") is not { } name) continue;
            switch (name)
            {
                case "PrefabSize": size = ParseInt3(value); break;
                case "YOffset": yOffset = ParseInt(value); break;
                case "RotationToFaceNorth": rotation = ParseInt(value); break;
                case "DifficultyTier": tier = ParseInt(value); break;
                case "Tags": tags = SplitList(value); break;
                case "Zoning": zoning = SplitList(value); break;
                case "ThemeTags": themeTags = SplitList(value.Replace(" ", "")); break;
                case "ThemeRepeatDistance": themeRepeat = ParseInt(value); break;
                case "DuplicateRepeatDistance": duplicateRepeat = ParseInt(value); break;
                case "TraderArea": trader = value.Trim().Equals("true", StringComparison.OrdinalIgnoreCase); break;
                default:
                    if (name.StartsWith("POIMarker", StringComparison.Ordinal)) props[name] = value;
                    break;
            }
        }
        if (size is null) return null;

        string jpg = Path.ChangeExtension(file, ".jpg");
        return new PrefabInfo(Path.GetFileNameWithoutExtension(file), Path.GetDirectoryName(file)!, size.Value,
            yOffset, rotation, tier, tags, File.Exists(jpg) ? jpg : null)
        {
            Zoning = zoning,
            Markers = ReadMarkers(props),
            TraderArea = trader,
            ThemeTags = themeTags,
            ThemeRepeatDistance = themeRepeat,
            DuplicateRepeatDistance = duplicateRepeat,
            DensityScore = density,
        };
    }

    /// <summary>Reads the parallel POIMarker* lists the way the game's marker list does: one marker per start.</summary>
    private static List<PrefabMarker> ReadMarkers(Dictionary<string, string> p)
    {
        if (!p.TryGetValue("POIMarkerStart", out var starts) || !p.TryGetValue("POIMarkerSize", out var sizes)) return [];
        string[] Get(string key, char separator) => p.TryGetValue(key, out var v) ? v.Split(separator) : [];
        var start = starts.Split('#');
        var size = sizes.Split('#');
        var type = Get("POIMarkerType", ',');
        var group = Get("POIMarkerGroup", ',');
        var tags = Get("POIMarkerTags", '#');
        var part = Get("POIMarkerPartToSpawn", ',');
        var rot = Get("POIMarkerPartRotations", ',');
        var chance = Get("POIMarkerPartSpawnChance", ',');

        var markers = new List<PrefabMarker>(start.Length);
        for (int i = 0; i < start.Length; i++)
        {
            if (ParseInt3(start[i]) is not { } s) continue;
            markers.Add(new PrefabMarker(
                s,
                i < size.Length && ParseInt3(size[i]) is { } sz ? sz : default,
                i < type.Length && Enum.TryParse<MarkerType>(type[i].Trim(), true, out var t) ? t : MarkerType.None,
                i < group.Length ? group[i].Trim() : "",
                i < tags.Length ? tags[i].Trim() : "",
                i < rot.Length ? ParseInt(rot[i]) : 0,
                i < chance.Length && float.TryParse(chance[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float c) ? c : 1,
                i < part.Length && part[i].Trim() is { Length: > 0 } name ? name : null));
        }
        return markers;
    }

    private static string[] SplitList(string value) =>
        value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static Int3? ParseInt3(string s)
    {
        var parts = s.Split(',');
        return parts.Length == 3 ? new Int3(ParseInt(parts[0]), ParseInt(parts[1]), ParseInt(parts[2])) : null;
    }
}
