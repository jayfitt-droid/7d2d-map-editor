using System.IO;
using System.Text.Json;
using MapEditor.Core.GameData;

namespace MapEditor.App;

/// <summary>Per-user settings, stored as JSON in %APPDATA%\7D2DMapEditor.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "settings.json");

    public string? GameFolder { get; set; }
    public string? LastWorldFolder { get; set; }
    public bool Autosave { get; set; } = true;

    /// <summary>Projects (.7dmap) and world folders opened or saved lately, newest first.</summary>
    public List<string> RecentFiles { get; set; } = [];

    public const int MaxRecentFiles = 10;

    /// <summary>Prefab names starred in the browser, kept by name so they carry across worlds and game updates.</summary>
    public List<string> FavouritePrefabs { get; set; } = [];

    /// <summary>Puts a project or world folder at the top of <see cref="RecentFiles"/>.</summary>
    public void AddRecent(string path)
    {
        path = Path.GetFullPath(path);
        RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        RecentFiles.Insert(0, path);
        if (RecentFiles.Count > MaxRecentFiles) RecentFiles.RemoveRange(MaxRecentFiles, RecentFiles.Count - MaxRecentFiles);
    }

    /// <summary>The ribbon's size: automatic, full or compact (View > Ribbon).</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<RibbonMode>))]
    public RibbonMode RibbonMode { get; set; } = RibbonMode.Auto;

    /// <summary>Overrides the release feed for update checks (see Updates).</summary>
    public string? UpdateUrl { get; set; }

    /// <summary>The main window's last size (DIPs) and whether it was maximized.</summary>
    public double WindowWidth { get; set; }
    public double WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public static AppSettings Load()
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            settings = new AppSettings();
        }

        if (settings.GameFolder is null || !GameInstall.IsValid(settings.GameFolder))
            settings.GameFolder = GameInstall.FindCandidates().FirstOrDefault();
        // Settings from before the recent list still know the last world.
        if (settings.RecentFiles.Count == 0 && settings.LastWorldFolder is { } last && (File.Exists(last) || Directory.Exists(last)))
            settings.AddRecent(last);
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
