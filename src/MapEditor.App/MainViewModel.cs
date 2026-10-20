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
    [ObservableProperty] private bool _showPrefabDetail = true;
    [ObservableProperty] private bool _showSpawns = true;
    [ObservableProperty] private bool _dimMap;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Layers))]
    private int _layersVersion;

    [ObservableProperty] private string _status = "Open a world with File > Open World (Ctrl+O).";
    [ObservableProperty] private string _cursorText = "";
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
            Traders = value.All.Where(p => p.IsTrader && !p.IsPart && !p.IsStreetTile)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (SelectedTrader is null || !Traders.Contains(SelectedTrader)) PickRandomTrader();
        }
    }

    private PrefabLibrary _prefabs = PrefabLibrary.Empty;
    public StampLibrary Stamps { get; set; } = StampLibrary.Empty;

    // ---- tools -------------------------------------------------------------

    public IReadOnlyList<ToolItem> NavigateTools { get; } =
    [
        new(EditorTool.Select, "Select", "V", Icons.Cursor, "Click a POI, trader, street tile or spawn point to select it, drag it to move it. Q/E turn it, PgUp/PgDn raise or lower it, Del removes it. Drag empty ground to pan."),
    ];

    public IReadOnlyList<ToolItem> TerrainTools { get; } =
    [
        new(EditorTool.Raise, "Raise", "R", Icons.Raise, "Drag to raise the ground. [ and ] change the size."),
        new(EditorTool.Lower, "Lower", "L", Icons.Lower, "Drag to lower the ground. [ and ] change the size."),
        new(EditorTool.Flatten, "Flatten", "F", Icons.Flatten, "Drag to level the ground; Ctrl+click samples the height."),
        new(EditorTool.Smooth, "Smooth", "S", Icons.Smooth, "Drag to soften bumps and steps."),
        new(EditorTool.Noise, "Roughen", "N", Icons.Roughen, "Drag to add natural-looking bumps."),
        new(EditorTool.Stamp, "Stamp", "T", Icons.Stamp, "Click to press a game stamp (mountain, lake...) into the terrain. Q and E turn it."),
    ];

    public IReadOnlyList<ToolItem> PaintTools { get; } =
    [
        new(EditorTool.Biome, "Paint biome", "B", Icons.Biome, "Drag to paint the biome picked in the palette. Biomes are stored in 8 m cells."),
        new(EditorTool.Radiation, "Paint radiation", "D", Icons.Radiation, "Drag to add radiation, Shift+drag to erase. Stored in 32 m cells."),
        new(EditorTool.Water, "Water fill", "W", Icons.Water, "Click low ground to flood it up to the level; Shift+click drains, Ctrl+click samples. Sea level is 30 m."),
    ];

    public IReadOnlyList<ToolItem> BuildTools { get; } =
    [
        new(EditorTool.Road, "Road", "O", Icons.Road, "Click to add points; double-click or Enter finishes, Backspace undoes a point, Esc cancels. Shift+drag erases."),
        new(EditorTool.Prefab, "Prefabs (POIs)", "I", Icons.Prefab, "Pick a prefab on the right, click to place; Q/E turn it. With none picked, click to select, drag to move, Del removes."),
        new(EditorTool.Trader, "Trader", "A", Icons.Trader, "Click to place the trader picked in the options (Random picks another); Q/E turn it."),
        new(EditorTool.Town, "Town", "G", Icons.Town, "Drag a rectangle to plan a town on whole 150 m cells, then Build (Enter) or Cancel (Esc)."),
        new(EditorTool.Spawn, "Spawn points", "H", Icons.Spawn, "Click to add a spawn point, drag to move, Q/E turn, Del removes. RWG makes 12."),
    ];

    /// <summary>Every tool, for shortcuts.</summary>
    public IEnumerable<ToolItem> Tools => NavigateTools.Concat(TerrainTools).Concat(PaintTools).Concat(BuildTools);

    public ToolItem CurrentTool => Tools.First(t => t.Tool == SelectedTool);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrushTool), nameof(IsStampTool), nameof(IsFlattenTool), nameof(IsNoiseTool),
        nameof(HasRadius), nameof(IsBiomeTool), nameof(IsRadiationTool), nameof(IsWaterTool), nameof(IsRoadTool),
        nameof(IsPrefabTool), nameof(IsTraderTool), nameof(IsPrefabsTool), nameof(PlacingPrefab), nameof(IsSpawnTool), nameof(IsSelectTool), nameof(ShowSelectionText), nameof(IsTownTool), nameof(ReservesInfoLines), nameof(ShowPrefabCard), nameof(CardPrefab), nameof(CardName), nameof(CardText), nameof(CardHasActions), nameof(CurrentTool),
        nameof(HasSize), nameof(SizeValue), nameof(SizeMin), nameof(SizeMax), nameof(SizeText),
        nameof(HasStrength), nameof(StrengthValue), nameof(StrengthMin), nameof(StrengthText), nameof(CurrentSwatch))]
    private EditorTool _selectedTool = EditorTool.Select;

    public bool IsBrushTool => SelectedTool is EditorTool.Raise or EditorTool.Lower or EditorTool.Flatten or EditorTool.Smooth or EditorTool.Noise;
    public bool IsStampTool => SelectedTool == EditorTool.Stamp;
    public bool IsFlattenTool => SelectedTool == EditorTool.Flatten;
    public bool IsNoiseTool => SelectedTool == EditorTool.Noise;
    public bool IsBiomeTool => SelectedTool == EditorTool.Biome;
    public bool IsRadiationTool => SelectedTool == EditorTool.Radiation;
    public bool IsWaterTool => SelectedTool == EditorTool.Water;
    public bool IsRoadTool => SelectedTool == EditorTool.Road;
    /// <summary>The Prefabs tool or the Trader tool, which places a trader the same way.</summary>
    public bool IsPrefabTool => SelectedTool is EditorTool.Prefab or EditorTool.Trader;
    public bool IsTraderTool => SelectedTool == EditorTool.Trader;
    public bool IsPrefabsTool => SelectedTool == EditorTool.Prefab;
    public bool IsSpawnTool => SelectedTool == EditorTool.Spawn;
    public bool IsSelectTool => SelectedTool == EditorTool.Select;

    /// <summary>The selection text under the ribbon: for spawns, which have no info card.</summary>
    public bool ShowSelectionText => IsSpawnTool || IsSelectTool && !HasPrefabSelection;
    public bool IsTownTool => SelectedTool == EditorTool.Town;

    /// <summary>
    /// Tools whose text under the ribbon changes as the mouse moves (a warning, a lot, what's replaced): it keeps room
    /// for its longest, so the map below doesn't jump.
    /// </summary>
    public bool ReservesInfoLines => IsPrefabTool || IsTownTool || IsSpawnTool || IsSelectTool;
    public bool HasRadius => IsBrushTool || IsBiomeTool || IsRadiationTool || IsRoadTool;

    [ObservableProperty] private double _brushRadius = 60;
    [ObservableProperty] private double _brushStrength = 0.5;
    [ObservableProperty] private double _flattenHeight = 40;
    [ObservableProperty] private bool _flattenLocked;
    /// <summary>Flatten heights picked from the map are rounded to whole metres, the height prefabs and levelled roads sit at.</summary>
    [ObservableProperty] private bool _flattenWholeMetres = true;
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

    partial void OnRoadTypeChanged(RoadType value)
    {
        if (!_applyingPreset) RoadWidth = value == RoadType.Asphalt ? 18 : 9;
    }

    /// <summary>
    /// Common road sizes: RWG's 4-lane highway (18 m) and 2-lane country road (9 m), lanes being 4.5 m, plus the
    /// asphalt widths measured on vanilla town tiles.
    /// </summary>
    public IReadOnlyList<RoadPreset> RoadPresets { get; } =
    [
        new("Highway", RoadType.Asphalt, 18),
        new("Main street", RoadType.Asphalt, 12),
        new("Town street", RoadType.Asphalt, 9),
        new("Residential", RoadType.Asphalt, 6),
        new("Country road", RoadType.Gravel, 9),
        new("Track", RoadType.Gravel, 4.5),
    ];

    private bool _applyingPreset;

    /// <summary>The preset matching the current surface and width, or null for a custom road.</summary>
    public RoadPreset? SelectedRoadPreset
    {
        get => RoadPresets.FirstOrDefault(p => p.Type == RoadType && p.Width == RoadWidth);
        set
        {
            if (value is null) return;
            _applyingPreset = true;
            try
            {
                RoadType = value.Type;
                RoadWidth = value.Width;
            }
            finally
            {
                _applyingPreset = false;
            }
        }
    }

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
    [NotifyPropertyChangedFor(nameof(ShowPrefabCard), nameof(ShowSelectionText))]
    private bool _hasPrefabSelection;

    /// <summary>
    /// The info card: the prefab selected on the map, or with the Trader tool the trader it will place, so the card
    /// always shows the trader picked in the options.
    /// </summary>
    public bool ShowPrefabCard => IsTraderTool ? SelectedTrader is not null : HasPrefabSelection && (IsPrefabTool || IsSelectTool);

    public PrefabInfo? CardPrefab => IsTraderTool ? SelectedTrader : SelectedMapPrefab;
    public string CardName => IsTraderTool ? SelectedTrader?.Name ?? "" : SelectedMapPrefabName;
    public string CardText => IsTraderTool ? PlacementText : SelectionText;

    /// <summary>Raise, lower, replace and delete act on a prefab on the map, so the trader card has none.</summary>
    public bool CardHasActions => !IsTraderTool;

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

    // ---- trader tool ---------------------------------------------------------

    /// <summary>The trader prefabs in the game (and mods): trader_bob, trader_hugh, trader_jen, trader_joel, trader_rekt.</summary>
    [ObservableProperty] private IReadOnlyList<PrefabInfo> _traders = [];

    /// <summary>The trader the Trader tool places.</summary>
    [ObservableProperty] private PrefabInfo? _selectedTrader;

    /// <summary>Picks a different trader at random (any trader if there is only one).</summary>
    public void PickRandomTrader()
    {
        var others = Traders.Where(t => t != SelectedTrader).ToList();
        SelectedTrader = others.Count > 0 ? others[Random.Shared.Next(others.Count)] : Traders.FirstOrDefault();
    }

    partial void OnSelectedTraderChanged(PrefabInfo? value)
    {
        if (value is not null && IsTraderTool) PrefabRotation = value.RotationToFaceNorth & 3;
    }

    /// <summary>What the Prefabs or Trader tool places on a click: the browser's pick, or the trader. Null to select.</summary>
    public PrefabInfo? PlacingPrefab => IsTraderTool ? SelectedTrader : IsPrefabsTool ? SelectedPrefabInfo : null;

    // ---- town tool -----------------------------------------------------------

    /// <summary>rwgmixer.xml's town planning data, read for the open world's size.</summary>
    public RwgMixer Mixer
    {
        get => _mixer;
        set
        {
            _mixer = value;
            var keep = SelectedTownship?.Name;
            LoadingTownships = true;
            try
            {
                Townships.Clear();
                foreach (var t in value.BuildableTownships) Townships.Add(t);
                SelectedTownship = Townships.FirstOrDefault(t => t.Name == keep) ?? Townships.FirstOrDefault();
            }
            finally
            {
                LoadingTownships = false;
            }
        }
    }

    /// <summary>True while <see cref="Townships"/> is being refilled, so that change is not taken for the user's pick.</summary>
    public bool LoadingTownships { get; private set; }

    /// <summary>
    /// The user chose a township in the list. Until then each new selection gets the township RWG would use there;
    /// after that the user's choice is kept.
    /// </summary>
    public bool TownshipPicked { get; set; }

    private RwgMixer _mixer = RwgMixer.Empty;

    public System.Collections.ObjectModel.ObservableCollection<TownshipType> Townships { get; } = [];
    [ObservableProperty] private TownshipType? _selectedTownship;
    [ObservableProperty] private int _townSeed = Random.Shared.Next(1, 1_000_000);
    [ObservableProperty] private bool _townReplace = true;
    [ObservableProperty] private bool _townRoads = true;
    [ObservableProperty] private LinkRoadStyle _townLinkStyle = LinkRoadStyle.JoinedRoad;
    [ObservableProperty] private bool _townParts = true;
    [ObservableProperty] private int _townTraders = 1;
    public IReadOnlyList<int> TraderCounts { get; } = [0, 1, 2, 3, 4, 5];

    /// <summary>Describes the selection and the planned town.</summary>
    [ObservableProperty] private string _townText = "";
    [ObservableProperty] private bool _hasTownPlan;

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

    // ---- side sliders (Paint's size and opacity) -----------------------------

    /// <summary>The size slider on the left edge: brush radius, stamp size or road width, by tool.</summary>
    public bool HasSize => HasRadius || IsStampTool;

    public double SizeMin => IsStampTool ? 32 : IsRoadTool ? 3 : 2;

    public double SizeMax => IsStampTool ? 4000 : IsRoadTool ? 30 : 800;

    public double SizeValue
    {
        get => IsStampTool ? StampSize : IsRoadTool ? RoadWidth : BrushRadius;
        set
        {
            if (IsStampTool) StampSize = value;
            else if (IsRoadTool) RoadWidth = Math.Round(value * 2) / 2;
            else BrushRadius = value;
        }
    }

    public string SizeText => IsStampTool ? $"{StampSize:0} m" : IsRoadTool ? $"{RoadWidth:0.#} m wide" : $"{BrushRadius * 2:0} m";

    /// <summary>The strength slider: brush strength or stamp strength.</summary>
    public bool HasStrength => IsBrushTool || IsStampTool;

    public double StrengthMin => IsStampTool ? 0.05 : 0.01;

    public double StrengthValue
    {
        get => IsStampTool ? StampStrength : BrushStrength;
        set
        {
            if (IsStampTool) StampStrength = value;
            else BrushStrength = value;
        }
    }

    public string StrengthText => $"{StrengthValue * 100:0}%";

    // ---- palette -------------------------------------------------------------

    public IReadOnlyList<Swatch> RoadSwatches { get; } =
    [
        new("Asphalt road", 0x3A3A40, null, RoadType.Asphalt),
        new("Gravel road", 0xB49A6A, null, RoadType.Gravel),
    ];

    /// <summary>The large swatch at the start of the palette: the road surface or biome the next stroke paints.</summary>
    public Swatch CurrentSwatch => IsRoadTool
        ? RoadSwatches.First(r => r.Road == RoadType)
        : SelectedBiome is { } b ? new Swatch(b.Name, b.Color, b, null) : new Swatch("None", 0x808080, null, null);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(BrushRadius) or nameof(StampSize) or nameof(RoadWidth):
                base.OnPropertyChanged(nameof(SizeValue));
                base.OnPropertyChanged(nameof(SizeText));
                if (e.PropertyName == nameof(RoadWidth)) base.OnPropertyChanged(nameof(SelectedRoadPreset));
                break;
            case nameof(BrushStrength) or nameof(StampStrength):
                base.OnPropertyChanged(nameof(StrengthValue));
                base.OnPropertyChanged(nameof(StrengthText));
                break;
            case nameof(SelectedTrader):
                base.OnPropertyChanged(nameof(ShowPrefabCard));
                base.OnPropertyChanged(nameof(CardPrefab));
                base.OnPropertyChanged(nameof(CardName));
                break;
            case nameof(SelectedMapPrefab):
                base.OnPropertyChanged(nameof(CardPrefab));
                break;
            case nameof(SelectedMapPrefabName):
                base.OnPropertyChanged(nameof(CardName));
                break;
            case nameof(SelectionText) or nameof(PlacementText):
                base.OnPropertyChanged(nameof(CardText));
                break;
            case nameof(SelectedBiome) or nameof(RoadType):
                base.OnPropertyChanged(nameof(CurrentSwatch));
                if (e.PropertyName == nameof(RoadType)) base.OnPropertyChanged(nameof(SelectedRoadPreset));
                break;
        }
    }

    // ---- view ----------------------------------------------------------------

    [ObservableProperty] private bool _showToolOptions = true;
    [ObservableProperty] private bool _showMiniMap = true;
    [ObservableProperty] private bool _showSidePanel;

    /// <summary>0 = Prefabs, 1 = Issues, 2 = World.</summary>
    [ObservableProperty] private int _sidePanelTab;

    [ObservableProperty] private string _worldSizeText = "";
    [ObservableProperty] private string _selectionSizeText = "";
    [ObservableProperty] private string _zoomPercentText = "";

    /// <summary>log2 of the map scale (DIPs per metre), for the zoom slider.</summary>
    [ObservableProperty] private double _zoomLog;

    [ObservableProperty] private bool _autosave = true;

    // ---- validation ----------------------------------------------------------

    public System.Collections.ObjectModel.ObservableCollection<MapEditor.Core.Validate.ValidationIssue> Issues { get; } = [];
    [ObservableProperty] private string _issuesText = "Press Validate (F7) to check the world.";

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

/// <summary>A palette entry: a biome or a road surface.</summary>
public sealed record RoadPreset(string Name, RoadType Type, double Width)
{
    public override string ToString() => $"{Name}  ({Width:0.#} m)";
}

public sealed record Swatch(string Label, uint Color, BiomeDefinition? Biome, RoadType? Road);
