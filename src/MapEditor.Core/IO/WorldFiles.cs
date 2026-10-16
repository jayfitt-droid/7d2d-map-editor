namespace MapEditor.Core.IO;

/// <summary>File names inside a game world folder.</summary>
public static class WorldFiles
{
    public const string Height = "dtm.raw";
    public const string Biomes = "biomes.png";
    public const string Roads = "splat3.png";
    public const string Water = "splat4.png";
    public const string Radiation = "radiation.png";
    public const string Prefabs = "prefabs.xml";
    public const string Spawns = "spawnpoints.xml";
    public const string MapInfo = "map_info.xml";
    public const string Level = "main.ttw";

    /// <summary>Files this editor models and rewrites itself.</summary>
    public static readonly IReadOnlySet<string> Modelled = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Height, Biomes, Roads, Water, Radiation, Prefabs, Spawns, MapInfo,
    };

    /// <summary>
    /// Caches the game rebuilds on first load (ChunkProviderGenerateWorldFromRaw.processFiles), plus
    /// previews that would be stale after an edit. They are never copied to an exported world.
    /// </summary>
    public static readonly IReadOnlySet<string> Derived = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "dtm_processed.raw", "splat3_processed.png", "splat4_processed.png",
        "splat3_half.png", "splat4_half.png", "checksums.txt", "preview.jpg", "main.ttw.bak",
    };

    public static bool IsDerived(string fileName) =>
        Derived.Contains(fileName) || fileName.EndsWith(".bak", StringComparison.OrdinalIgnoreCase);
}
