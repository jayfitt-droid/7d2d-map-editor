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

    private static readonly HashSet<string> DeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>
    /// Whether <paramref name="name"/> can be written as an extra file inside a world folder: a plain file name (no
    /// folders, drive or parent references, no Windows device name) that isn't a file the editor writes itself.
    /// Names come from project files, which may have been made by someone else.
    /// </summary>
    public static bool IsSafeExtraName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200) return false;
        if (name != Path.GetFileName(name) || Path.IsPathRooted(name)) return false;
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(':')) return false;
        if (name is "." or ".." || name.EndsWith('.') || name.EndsWith(' ')) return false;
        if (DeviceNames.Contains(Path.GetFileNameWithoutExtension(name))) return false;
        return !Modelled.Contains(name) && !IsDerived(name);
    }
}
