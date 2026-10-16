using CommunityToolkit.Mvvm.ComponentModel;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
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

    public BiomeTable Biomes
    {
        get => _biomes;
        set
        {
            _biomes = value;
            // Biomes the editor can paint; "underwater" is the game's own lake-bed biome, not a land biome.
            var keep = SelectedBiome?.Name;
            BiomeChoices.Clear();
            foreach (var b in value.Biomes.Where(b => !b.Name.Equals("underwater", StringComparison.OrdinalIgnoreCase)))
                BiomeChoices.Add(b);
            SelectedBiome = BiomeChoices.FirstOrDefault(b => b.Name == keep) ?? BiomeChoices.FirstOrDefault();
        }
    }

    private BiomeTable _biomes = BiomeTable.Vanilla;

    public MainViewModel() => Biomes = BiomeTable.Vanilla;
    public PrefabLibrary Prefabs { get; set; } = PrefabLibrary.Empty;
    public StampLibrary Stamps { get; set; } = StampLibrary.Empty;

    // ---- tools -------------------------------------------------------------

    public IReadOnlyList<ToolItem> Tools { get; } =
    [
        new(EditorTool.Pan, "Pan", "P"),
        new(EditorTool.Raise, "Raise", "R"),
        new(EditorTool.Lower, "Lower", "L"),
        new(EditorTool.Flatten, "Flatten", "F"),
        new(EditorTool.Smooth, "Smooth", "S"),
        new(EditorTool.Noise, "Roughen", "N"),
        new(EditorTool.Stamp, "Stamp", "T"),
        new(EditorTool.Biome, "Paint biome", "B"),
        new(EditorTool.Radiation, "Paint radiation", "D"),
        new(EditorTool.Water, "Water fill", "W"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrushTool), nameof(IsStampTool), nameof(IsFlattenTool), nameof(IsNoiseTool),
        nameof(HasRadius), nameof(IsBiomeTool), nameof(IsRadiationTool), nameof(IsWaterTool))]
    private EditorTool _selectedTool = EditorTool.Pan;

    public bool IsBrushTool => SelectedTool is EditorTool.Raise or EditorTool.Lower or EditorTool.Flatten or EditorTool.Smooth or EditorTool.Noise;
    public bool IsStampTool => SelectedTool == EditorTool.Stamp;
    public bool IsFlattenTool => SelectedTool == EditorTool.Flatten;
    public bool IsNoiseTool => SelectedTool == EditorTool.Noise;
    public bool IsBiomeTool => SelectedTool == EditorTool.Biome;
    public bool IsRadiationTool => SelectedTool == EditorTool.Radiation;
    public bool IsWaterTool => SelectedTool == EditorTool.Water;
    public bool HasRadius => IsBrushTool || IsBiomeTool || IsRadiationTool;

    [ObservableProperty] private double _brushRadius = 60;
    [ObservableProperty] private double _brushStrength = 0.5;
    [ObservableProperty] private double _flattenHeight = 40;
    [ObservableProperty] private bool _flattenLocked;
    [ObservableProperty] private double _noiseScale = 48;

    public IReadOnlyList<BrushShape> BrushShapes { get; } = Enum.GetValues<BrushShape>();
    public IReadOnlyList<BrushFalloff> BrushFalloffs { get; } = Enum.GetValues<BrushFalloff>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSquareBrush))]
    private BrushShape _brushShape;

    [ObservableProperty] private BrushFalloff _brushFalloff;
    [ObservableProperty] private double _brushRotation;

    public bool IsSquareBrush => BrushShape == BrushShape.Square;

    /// <summary>The tip the terrain and layer brushes paint with.</summary>
    public BrushTip BrushTip => new((float)BrushRadius, BrushShape, BrushFalloff, (float)BrushRotation);

    public System.Collections.ObjectModel.ObservableCollection<string> StampNames { get; } = [];
    [ObservableProperty] private string? _selectedStamp;
    [ObservableProperty] private double _stampSize = 512;
    [ObservableProperty] private double _stampRotation;
    [ObservableProperty] private double _stampStrength = 1;
    [ObservableProperty] private bool _stampAdditive;
    [ObservableProperty] private bool _stampWater;

    partial void OnSelectedStampChanged(string? value)
    {
        // Lakes, rivers and ocean borders come with water by default; terrain stamps do not.
        if (value is not null)
            StampWater = value.StartsWith("lake", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("river", StringComparison.OrdinalIgnoreCase)
                || value.StartsWith("water", StringComparison.OrdinalIgnoreCase);
    }

    public System.Collections.ObjectModel.ObservableCollection<BiomeDefinition> BiomeChoices { get; } = [];
    [ObservableProperty] private BiomeDefinition? _selectedBiome;

    /// <summary>Water surface for the fill tool, in whole metres (splat4 stores a byte).</summary>
    [ObservableProperty] private double _waterLevel = 30;

    [ObservableProperty] private string _undoText = "Undo";
    [ObservableProperty] private string _redoText = "Redo";
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;

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
