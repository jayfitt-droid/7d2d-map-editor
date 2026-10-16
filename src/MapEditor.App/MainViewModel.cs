using CommunityToolkit.Mvvm.ComponentModel;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Rendering;

namespace MapEditor.App;

public sealed partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private bool _showHillshade = true;
    [ObservableProperty] private bool _showBiomes = true;
    [ObservableProperty] private bool _showWater = true;
    [ObservableProperty] private bool _showRoads = true;
    [ObservableProperty] private bool _showRadiation = true;
    [ObservableProperty] private bool _showPrefabs = true;
    [ObservableProperty] private bool _showSpawns = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layers))]
    private int _layersVersion;

    [ObservableProperty] private string _status = "Open a world with File > Open World (Ctrl+O).";
    [ObservableProperty] private string _cursorText = "";
    [ObservableProperty] private string _zoomText = "";
    [ObservableProperty] private string _worldSummary = "";
    [ObservableProperty] private string _gameFolderText = "";
    [ObservableProperty] private bool _isBusy;

    public BiomeTable Biomes { get; set; } = BiomeTable.Vanilla;
    public PrefabLibrary Prefabs { get; set; } = PrefabLibrary.Empty;

    public MapLayers Layers =>
        (ShowHillshade ? MapLayers.Hillshade : 0) |
        (ShowBiomes ? MapLayers.Biomes : 0) |
        (ShowWater ? MapLayers.Water : 0) |
        (ShowRoads ? MapLayers.Roads : 0) |
        (ShowRadiation ? MapLayers.Radiation : 0);

    partial void OnShowHillshadeChanged(bool value) => LayersVersion++;
    partial void OnShowBiomesChanged(bool value) => LayersVersion++;
    partial void OnShowWaterChanged(bool value) => LayersVersion++;
    partial void OnShowRoadsChanged(bool value) => LayersVersion++;
    partial void OnShowRadiationChanged(bool value) => LayersVersion++;

    /// <summary>Formats the cursor readout in the game's own terms (E/W, N/S, metres).</summary>
    public static string FormatPoint(PointInfo p)
    {
        string ew = p.WorldX >= 0 ? $"{p.WorldX} E" : $"{-p.WorldX} W";
        string ns = p.WorldZ >= 0 ? $"{p.WorldZ} N" : $"{-p.WorldZ} S";
        var parts = new List<string> { $"{ew}, {ns}", $"height {p.HeightMetres:0.0} m" };
        if (p.Biome is not null) parts.Add(p.Biome);
        if (p.Road != RoadType.None) parts.Add(p.Road.ToString().ToLowerInvariant() + " road");
        if (p.WaterSurface > 0 && p.HeightMetres <= p.WaterSurface + 0.5f) parts.Add($"water {p.WaterSurface - p.HeightMetres:0.0} m deep");
        if (p.Radiation) parts.Add("radiation");
        if (p.Prefab is not null) parts.Add($"{p.Prefab.Name} (rot {p.Prefab.Rotation})");
        return string.Join("  |  ", parts);
    }
}
