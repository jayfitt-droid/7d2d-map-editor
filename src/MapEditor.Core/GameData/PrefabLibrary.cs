using System.Collections.Concurrent;
using System.Globalization;
using System.Xml;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameData;

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
    /// <summary>Footprint on the ground after rotating by <paramref name="rotation"/> quarter turns.</summary>
    public (int X, int Z) Footprint(int rotation) => (rotation & 1) == 0 ? (Size.X, Size.Z) : (Size.Z, Size.X);
}

/// <summary>All prefabs available to a game install (vanilla Data/Prefabs plus Mods/*/Prefabs).</summary>
public sealed class PrefabLibrary
{
    private readonly Dictionary<string, PrefabInfo> _byName;

    private PrefabLibrary(Dictionary<string, PrefabInfo> byName) => _byName = byName;

    public static PrefabLibrary Empty { get; } = new(new(StringComparer.OrdinalIgnoreCase));

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

    private static PrefabInfo? TryRead(string file)
    {
        Int3? size = null;
        int yOffset = 0, rotation = 0, tier = 0;
        IReadOnlyList<string> tags = [];

        using var reader = XmlReader.Create(file, new XmlReaderSettings { IgnoreComments = true });
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.Name != "property" || reader.Depth != 1) continue;
            string? value = reader.GetAttribute("value");
            if (value is null) continue;
            switch (reader.GetAttribute("name"))
            {
                case "PrefabSize": size = ParseInt3(value); break;
                case "YOffset": yOffset = ParseInt(value); break;
                case "RotationToFaceNorth": rotation = ParseInt(value); break;
                case "DifficultyTier": tier = ParseInt(value); break;
                case "Tags": tags = value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
            }
        }
        if (size is null) return null;

        string jpg = Path.ChangeExtension(file, ".jpg");
        return new PrefabInfo(Path.GetFileNameWithoutExtension(file), Path.GetDirectoryName(file)!, size.Value,
            yOffset, rotation, tier, tags, File.Exists(jpg) ? jpg : null);
    }

    private static int ParseInt(string s) =>
        int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

    private static Int3? ParseInt3(string s)
    {
        var parts = s.Split(',');
        return parts.Length == 3 ? new Int3(ParseInt(parts[0]), ParseInt(parts[1]), ParseInt(parts[2])) : null;
    }
}
