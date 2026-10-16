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
    [ObservableProperty] private bool _dimMap;

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
    public PrefabLibrary Prefabs
    {
        get => _prefabs;
        set
        {
            _prefabs = value;
            RefreshPrefabResults();
        }
    }

    private PrefabLibrary _prefabs = PrefabLibrary.Empty;
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
        new(EditorTool.Road, "Road", "O"),
        new(EditorTool.Prefab, "Prefabs (POIs)", "I"),
        new(EditorTool.Spawn, "Spawn points", "H"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrushTool), nameof(IsStampTool), nameof(IsFlattenTool), nameof(IsNoiseTool),
        nameof(HasRadius), nameof(IsBiomeTool), nameof(IsRadiationTool), nameof(IsWaterTool), nameof(IsRoadTool),
        nameof(IsPrefabTool), nameof(IsSpawnTool), nameof(ShowPrefabCard))]
    private EditorTool _selectedTool = EditorTool.Pan;

    public bool IsBrushTool => SelectedTool is EditorTool.Raise or EditorTool.Lower or EditorTool.Flatten or EditorTool.Smooth or EditorTool.Noise;
    public bool IsStampTool => SelectedTool == EditorTool.Stamp;
    public bool IsFlattenTool => SelectedTool == EditorTool.Flatten;
    public bool IsNoiseTool => SelectedTool == EditorTool.Noise;
    public bool IsBiomeTool => SelectedTool == EditorTool.Biome;
    public bool IsRadiationTool => SelectedTool == EditorTool.Radiation;
    public bool IsWaterTool => SelectedTool == EditorTool.Water;
    public bool IsRoadTool => SelectedTool == EditorTool.Road;
    public bool IsPrefabTool => SelectedTool == EditorTool.Prefab;
    public bool IsSpawnTool => SelectedTool == EditorTool.Spawn;
    public bool HasRadius => IsBrushTool || IsBiomeTool || IsRadiationTool || IsRoadTool;

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

    public IReadOnlyList<RoadType> RoadTypes { get; } = [RoadType.Asphalt, RoadType.Gravel];
    [ObservableProperty] private RoadType _roadType = RoadType.Asphalt;

    /// <summary>Full width of the road surface in metres (RWG: 18 m highways, 9 m country roads).</summary>
    [ObservableProperty] private double _roadWidth = 18;

    [ObservableProperty] private bool _roadGrade = true;

    partial void OnRoadTypeChanged(RoadType value) => RoadWidth = value == RoadType.Asphalt ? 18 : 9;

    // ---- prefab browser ----------------------------------------------------

    public IReadOnlyList<string> PrefabCategories { get; } = ["POIs", "Street tiles", "Parts", "All"];
    public IReadOnlyList<string> PrefabTiers { get; } = ["Any tier", "Tier 0", "Tier 1", "Tier 2", "Tier 3", "Tier 4", "Tier 5"];

    /// <summary>Footprint limits matching RWG's lot sizes (Marker.MarkerSizes), so a POI can be picked for a lot.</summary>
    public IReadOnlyList<string> PrefabSizes { get; } = ["Any size", "Fits 25 m lot", "Fits 42 m lot", "Fits 60 m lot", "Fits 100 m lot", "Over 100 m"];

    [ObservableProperty] private string _prefabSearch = "";
    [ObservableProperty] private string _prefabCategory = "POIs";
    [ObservableProperty] private string _prefabTier = "Any tier";
    [ObservableProperty] private string _prefabSize = "Any size";
    [ObservableProperty] private IReadOnlyList<PrefabInfo> _prefabResults = [];
    [ObservableProperty] private string _prefabResultsText = "";

    /// <summary>The prefab chosen in the browser; while set, clicks on the map place it.</summary>
    [ObservableProperty] private PrefabInfo? _selectedPrefabInfo;

    /// <summary>Quarter turns for the prefab being placed (0-3, as written to prefabs.xml).</summary>
    [ObservableProperty] private int _prefabRotation;

    [ObservableProperty] private bool _prefabFlatten = true;
    [ObservableProperty] private bool _prefabAddParts = true;

    /// <summary>Describes the selected prefab or spawn on the map.</summary>
    [ObservableProperty] private string _selectionText = "";

    /// <summary>Describes the prefab being placed or dragged.</summary>
    [ObservableProperty] private string _placementText = "";

    /// <summary>The prefab selected on the map, for the info card; null if it is not in the game folder.</summary>
    [ObservableProperty] private PrefabInfo? _selectedMapPrefab;
    [ObservableProperty] private string _selectedMapPrefabName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPrefabCard))]
    private bool _hasPrefabSelection;

    public bool ShowPrefabCard => HasPrefabSelection && IsPrefabTool;

    partial void OnPrefabSearchChanged(string value) => RefreshPrefabResults();
    partial void OnPrefabCategoryChanged(string value) => RefreshPrefabResults();
    partial void OnPrefabTierChanged(string value) => RefreshPrefabResults();
    partial void OnPrefabSizeChanged(string value) => RefreshPrefabResults();

    private void RefreshPrefabResults()
    {
        string[] words = PrefabSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int tier = PrefabTiers.ToList().IndexOf(PrefabTier) - 1;
        int sizeIndex = PrefabSizes.ToList().IndexOf(PrefabSize);
        int[] limits = [0, 25, 42, 60, 100];
        bool Matches(PrefabInfo p)
        {
            bool category = PrefabCategory switch
            {
                "POIs" => !p.IsStreetTile && !p.IsPart && !p.IsInternal,
                "Street tiles" => p.IsStreetTile,
                "Parts" => p.IsPart,
                _ => true,
            };
            if (!category || (tier >= 0 && p.DifficultyTier != tier)) return false;
            int big = Math.Max(p.Size.X, p.Size.Z);
            if (sizeIndex is >= 1 and <= 4 && big > limits[sizeIndex]) return false;
            if (sizeIndex == 5 && big <= 100) return false;
            // Every word must match the name, a tag or a zoning entry.
            return words.All(w => p.Name.Contains(w, StringComparison.OrdinalIgnoreCase)
                                  || p.Tags.Any(t => t.Contains(w, StringComparison.OrdinalIgnoreCase))
                                  || p.Zoning.Any(z => z.Contains(w, StringComparison.OrdinalIgnoreCase)));
        }
        PrefabResults = _prefabs.All.Where(Matches).OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
        PrefabResultsText = _prefabs.Count == 0
            ? "No prefabs loaded. Set the game folder under Settings."
            : $"{PrefabResults.Count:N0} of {_prefabs.Count:N0} prefabs";
        if (SelectedPrefabInfo is not null && !PrefabResults.Contains(SelectedPrefabInfo)) SelectedPrefabInfo = null;
    }

    public bool CanReplace => SelectedPrefabInfo is not null;

    public string ReplaceLabel => SelectedPrefabInfo is { } p ? "Replace with " + p.Name : "Replace: pick a prefab in the list first";

    partial void OnSelectedPrefabInfoChanged(PrefabInfo? value)
    {
        OnPropertyChanged(nameof(CanReplace));
        OnPropertyChanged(nameof(ReplaceLabel));
        // Start a new pick facing north, the way RWG lines POIs up with their street.
        if (value is not null) PrefabRotation = value.RotationToFaceNorth & 3;
    }

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
