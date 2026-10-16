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
        return settings;
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
