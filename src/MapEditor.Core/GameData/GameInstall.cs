using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace MapEditor.Core.GameData;

/// <summary>A 7 Days to Die install. The editor reads stamps, prefabs and config from its Data folder.</summary>
public sealed partial class GameInstall
{
    private GameInstall(string root) => Root = root;

    public string Root { get; }
    public string DataDir => Path.Combine(Root, "Data");
    public string ConfigDir => Path.Combine(DataDir, "Config");
    public string StampsDir => Path.Combine(DataDir, "Stamps");
    public string PrefabsDir => Path.Combine(DataDir, "Prefabs");
    public string WorldsDir => Path.Combine(DataDir, "Worlds");

    /// <summary>Where the game saves generated worlds for the current user.</summary>
    public static string UserGeneratedWorldsDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7DaysToDie", "GeneratedWorlds");

    /// <summary>True if the folder looks like a game install (has the config this editor needs).</summary>
    public static bool IsValid(string root) =>
        File.Exists(Path.Combine(root, "Data", "Config", "biomes.xml")) &&
        File.Exists(Path.Combine(root, "Data", "Config", "rwgmixer.xml"));

    public static GameInstall Open(string root)
    {
        if (!IsValid(root)) throw new DirectoryNotFoundException($"'{root}' is not a 7 Days to Die install (Data/Config is missing).");
        return new GameInstall(Path.GetFullPath(root));
    }

    /// <summary>Finds installs from Steam's library folders. The user can always pick a folder instead.</summary>
    public static IReadOnlyList<string> FindCandidates()
    {
        var found = new List<string>();
        foreach (var library in SteamLibraries())
        {
            string path = Path.Combine(library, "steamapps", "common", "7 Days To Die");
            if (IsValid(path) && !found.Contains(path, StringComparer.OrdinalIgnoreCase)) found.Add(path);
        }
        return found;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        if (OperatingSystem.IsWindows() &&
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string steamPath)
            roots.Add(steamPath.Replace('/', Path.DirectorySeparatorChar));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam"));

        var libraries = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root)) continue;
            libraries.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in VdfPath().Matches(File.ReadAllText(vdf)))
                libraries.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("\"path\"\\s+\"([^\"]+)\"")]
    private static partial Regex VdfPath();
}
