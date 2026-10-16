using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MapEditor.App.Editing;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;
using Microsoft.Win32;

namespace MapEditor.App;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NewWorldCommand = new("New World", nameof(NewWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenWorldCommand = new("Open World", nameof(OpenWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveCommand = new("Save World", nameof(SaveCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveAsCommand = new("Save World As", nameof(SaveAsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand UndoCommand = new("Undo", nameof(UndoCommand), typeof(MainWindow));
    public static readonly RoutedUICommand RedoCommand = new("Redo", nameof(RedoCommand), typeof(MainWindow));

    private readonly MainViewModel _vm = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UndoStack _undo = new();
    private readonly TerrainToolController _tools;
    private Task _gameDataLoad = Task.CompletedTask;
    private GameInstall? _install;

    private World? _world;
    private string? _sourcePath; // folder the world was opened from
    private string? _savePath;   // folder Ctrl+S writes to (null until Save As for opened or new worlds)
    private bool _dirty;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Overview.Target = Map;
        Map.CursorMoved += Map_CursorMoved;
        Map.ViewChanged += (_, _) => UpdateZoomText();
        _vm.PropertyChanged += Vm_PropertyChanged;

        _tools = new TerrainToolController(Map, _vm, _undo);
        _tools.Edited += (_, _) => OnWorldEdited();
        _undo.Changed += (_, _) => UpdateUndoText();

        Loaded += async (_, _) =>
        {
            _gameDataLoad = LoadGameDataAsync();
            // MapEditor.exe "<world folder>" opens that world straight away.
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1])) await OpenWorldAsync(args[1]);
        };
        Closing += MainWindow_Closing;
    }

    // ---- game data ---------------------------------------------------------

    private async Task LoadGameDataAsync()
    {
        _install = null;
        if (_settings.GameFolder is not { } folder || !GameInstall.IsValid(folder))
        {
            _vm.GameFolderText = "Not set. Use Settings > Game Folder so biomes, prefabs and stamps are available.";
            _vm.Biomes = BiomeTable.Vanilla;
            _vm.Prefabs = PrefabLibrary.Empty;
            _vm.Stamps = StampLibrary.Empty;
            _vm.StampNames.Clear();
            return;
        }

        _vm.GameFolderText = folder + "\n(loading prefabs...)";
        var sw = Stopwatch.StartNew();
        var (install, biomes, prefabs, stamps) = await Task.Run(() =>
        {
            var gi = GameInstall.Open(folder);
            return (gi, BiomeTable.Load(gi), PrefabLibrary.Load(gi), StampLibrary.Load(gi));
        });
        _install = install;
        _vm.Biomes = biomes;
        _vm.Prefabs = prefabs;
        _vm.Stamps = stamps;
        _vm.StampNames.Clear();
        foreach (var name in stamps.Names) _vm.StampNames.Add(name);
        _vm.SelectedStamp ??= _vm.StampNames.FirstOrDefault(n => n.StartsWith("mountains")) ?? _vm.StampNames.FirstOrDefault();
        Map.Prefabs = prefabs;
        Map.InvalidateVisual();
        _vm.GameFolderText = $"{folder}\n{prefabs.Count:N0} prefabs, {stamps.Names.Count} stamps, {biomes.Biomes.Count} biomes ({sw.Elapsed.TotalSeconds:0.0} s)";
    }

    private async void GameFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the 7 Days to Die game folder (the one containing Data)",
            InitialDirectory = _settings.GameFolder ?? "",
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!GameInstall.IsValid(dialog.FolderName))
        {
            MessageBox.Show(this, "That folder has no Data\\Config\\biomes.xml, so it doesn't look like a 7 Days to Die install.",
                "Game Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _settings.GameFolder = dialog.FolderName;
        _settings.Save();
        await (_gameDataLoad = LoadGameDataAsync());
        if (_world is not null) ShowWorld(_world, keepView: true);
    }

    // ---- new / open --------------------------------------------------------

    private async void NewWorld_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        await _gameDataLoad;
        var dialog = new NewWorldDialog(_vm.Biomes) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Options is not { } options) return;

        _vm.IsBusy = true;
        _vm.Status = "Creating " + options.Name + "...";
        try
        {
            var template = LevelFile.FindTemplate(_install);
            var stamps = _vm.Stamps;
            var world = await Task.Run(() => NewWorldGenerator.Create(options, stamps, template is null ? null : File.ReadAllBytes(template.Path)));
            SetWorld(world, sourcePath: null, dirty: true);
            _vm.Status = template is null
                ? "Created. Note: no main.ttw template was found; generate any world in the game once before saving."
                : $"Created {options.Name} ({options.Size} m). Save it with Ctrl+S.";
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private async void OpenWorld_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var dialog = new OpenFolderDialog
        {
            Title = "Select a 7 Days to Die world folder",
            InitialDirectory = _settings.LastWorldFolder is { } last && Directory.Exists(Path.GetDirectoryName(last))
                ? Path.GetDirectoryName(last)!
                : GameInstall.UserGeneratedWorldsDir,
        };
        if (dialog.ShowDialog(this) != true) return;
        await OpenWorldAsync(dialog.FolderName);
    }

    private async Task OpenWorldAsync(string folder)
    {
        _vm.IsBusy = true;
        _vm.Status = "Loading " + Path.GetFileName(folder) + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            var world = await Task.Run(() => WorldReader.Read(folder));
            await _gameDataLoad;
            SetWorld(world, folder, dirty: false);
            _vm.Status = $"Loaded {world.Name} in {sw.Elapsed.TotalSeconds:0.0} s";
            _settings.LastWorldFolder = folder;
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            _vm.Status = "Failed to load world";
            MessageBox.Show(this, ex.Message, "Open World", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private void SetWorld(World world, string? sourcePath, bool dirty)
    {
        _world = world;
        _sourcePath = sourcePath;
        _savePath = null;
        _undo.Clear();
        _tools.CancelRoad();
        _tools.World = world;
        _dirty = dirty;
        ShowWorld(world, keepView: false);
    }

    private void ShowWorld(World world, bool keepView)
    {
        var renderer = new MapRenderer(world, _vm.Biomes);
        Map.Prefabs = _vm.Prefabs;
        Map.Renderer = renderer;
        Overview.SetRenderer(renderer, _vm.Layers);
        _vm.WorldSummary = WorldSummaryFormatter.Format(world);
        UpdateTitle();
        UpdateZoomText();
        UpdateToolState();
        Map.Focus();
    }

    // ---- save --------------------------------------------------------------

    private void HasWorld_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _world is not null && !_vm.IsBusy;

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: true);

    /// <summary>Saves the world; returns false if the user cancelled or the save failed.</summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_world is not { } world) return false;
        string? folder = saveAs ? null : _savePath;
        if (folder is null)
        {
            string location = Path.GetDirectoryName(_savePath ?? _sourcePath ?? "") is { Length: > 0 } dir ? dir : GameInstall.UserGeneratedWorldsDir;
            var dialog = new SaveWorldDialog(world.Name, location) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Folder is null) return false;
            folder = dialog.Folder;
        }

        bool newGuid = !string.Equals(Path.GetFullPath(folder), _sourcePath is null ? null : Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase)
                       && folder != _savePath;
        byte[]? template = null;
        if (!world.ExtraFiles.ContainsKey(WorldFiles.Level))
        {
            var found = LevelFile.FindTemplate(_install);
            if (found is null)
            {
                MessageBox.Show(this, "The world needs a main.ttw and none could be found to copy. Generate any world in the game once, then save again.",
                    "Save World", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            template = File.ReadAllBytes(found.Path);
        }

        var addedSpawn = SpawnHelper.EnsureOne(world);
        if (addedSpawn is not null) Map.InvalidateVisual();

        _vm.IsBusy = true;
        _vm.Status = "Saving to " + folder + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            world.Name = Path.GetFileName(folder);
            var options = new WorldWriteOptions { Overwrite = true, LevelTemplate = template, NewLevelGuid = newGuid };
            await Task.Run(() => WorldWriter.Write(world, folder, options));
            _savePath = folder;
            _sourcePath ??= folder;
            _dirty = false;
            _settings.LastWorldFolder = folder;
            _settings.Save();
            _vm.WorldSummary = WorldSummaryFormatter.Format(world);
            UpdateTitle();
            _vm.Status = $"Saved {world.Name} in {sw.Elapsed.TotalSeconds:0.0} s. The game rebuilds its caches on first load."
                + (addedSpawn is null ? "" : $" Added a spawn point at {addedSpawn.X:0}, {addedSpawn.Z:0} because the world had none.");
            WarnAboutExistingGames(world.Name);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _vm.Status = "Save failed";
            MessageBox.Show(this, ex.Message, "Save World", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    private readonly HashSet<string> _warnedAboutGames = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Saved games keep the chunks they have already generated, so edits only show up in places a game has
    /// not visited yet. Says so once per world per session.
    /// </summary>
    private void WarnAboutExistingGames(string worldName)
    {
        var games = GameInstall.SavedGamesOn(worldName);
        if (games.Count == 0 || !_warnedAboutGames.Add(worldName)) return;
        string list = string.Join("\n", games.Take(8).Select(g => "  • " + g)) + (games.Count > 8 ? $"\n  … and {games.Count - 8} more" : "");
        MessageBox.Show(this,
            $"The game already has saved games on {worldName}:\n\n{list}\n\n" +
            "Those games keep the terrain, plants and water they have already generated, so your changes only appear " +
            "where they have not been yet. Start a new game on this world to see the changes everywhere.\n\n" +
            $"Saved games are in {Path.Combine(GameInstall.UserSavesDir, worldName)}.",
            "Existing games on this world", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>Asks about unsaved changes. Returns false if the user wants to stay.</summary>
    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_world is null || !_dirty) return true;
        var answer = MessageBox.Show(this, $"Save changes to {_world.Name}?", "7D2D Map Editor", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return answer switch
        {
            MessageBoxResult.Yes => await SaveAsync(saveAs: false),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private bool _closeConfirmed;

    private async void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_closeConfirmed || _world is null || !_dirty) return;
        e.Cancel = true;
        if (await ConfirmDiscardAsync())
        {
            _closeConfirmed = true;
            // "Don't save" completes synchronously, still inside this Closing event, where Close() throws;
            // close once the event has returned instead.
            _ = Dispatcher.BeginInvoke(Close);
        }
    }

    // ---- heightmap import/export -------------------------------------------

    private void ImportHeightmap_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world) return;
        var dialog = new OpenFileDialog { Title = "Import heightmap", Filter = "Heightmaps (*.png;*.raw)|*.png;*.raw" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var imported = HeightmapFile.Import(dialog.FileName, world.Size);
            var session = new EditSession("Import Heightmap");
            var all = new GridRect(0, 0, world.Size, world.Size);
            session.Touch(world.Height, all);
            imported.Data.CopyTo(world.Height.Data, 0);
            _undo.Push(session);
            Map.InvalidateRegion(all);
            OnWorldEdited();
            _vm.Status = "Imported " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or NotSupportedException)
        {
            MessageBox.Show(this, ex.Message, "Import Heightmap", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ExportHeightmap_Click(object sender, RoutedEventArgs e)
    {
        if (_world is not { } world) return;
        var dialog = new SaveFileDialog
        {
            Title = "Export heightmap",
            Filter = "16-bit PNG (*.png)|*.png|Game RAW (*.raw)|*.raw",
            FileName = world.Name + "_height",
        };
        if (dialog.ShowDialog(this) != true) return;
        HeightmapFile.Export(world.Height, dialog.FileName);
        _vm.Status = "Exported " + Path.GetFileName(dialog.FileName);
    }

    // ---- undo --------------------------------------------------------------

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _undo.CanUndo && !_tools.IsStroking && !_vm.IsBusy;

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _undo.CanRedo && !_tools.IsStroking && !_vm.IsBusy;

    private void Undo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_undo.Undo() is { } step) AfterUndoRedo(step, "Undid");
    }

    private void Redo_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (_undo.Redo() is { } step) AfterUndoRedo(step, "Redid");
    }

    private void AfterUndoRedo(UndoStep step, string verb)
    {
        Map.InvalidateRegion(step.Bounds);
        OnWorldEdited();
        _vm.Status = $"{verb} {step.Name}";
    }

    private void UpdateUndoText()
    {
        _vm.UndoText = _undo.UndoName is { } u ? $"Undo {u}" : "Undo";
        _vm.RedoText = _undo.RedoName is { } r ? $"Redo {r}" : "Redo";
    }

    private void OnWorldEdited()
    {
        _dirty = true;
        UpdateTitle();
        if (Map.Renderer is { } renderer) Overview.SetRenderer(renderer, _vm.Layers);
    }

    private void UpdateTitle() =>
        Title = _world is null ? "7D2D Map Editor" : $"{(_dirty ? "*" : "")}{_world.Name} - 7D2D Map Editor";

    // ---- tools and view ----------------------------------------------------

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Layers) when Map.Renderer is { } renderer:
                Overview.SetRenderer(renderer, _vm.Layers);
                break;
            case nameof(MainViewModel.SelectedTool):
                if (!_vm.IsRoadTool) _tools.CancelRoad();
                UpdateToolState();
                break;
            case nameof(MainViewModel.RoadType):
            case nameof(MainViewModel.RoadWidth):
                _tools.RefreshRoadPreview();
                UpdateToolState();
                break;
            case nameof(MainViewModel.BrushRadius):
            case nameof(MainViewModel.StampSize):
            case nameof(MainViewModel.StampRotation):
            case nameof(MainViewModel.BrushShape):
            case nameof(MainViewModel.BrushRotation):
                UpdateToolState();
                break;
        }
    }

    private void UpdateToolState()
    {
        Map.ToolActive = _vm.SelectedTool != EditorTool.Pan;
        Map.BrushCursor = _vm.SelectedTool switch
        {
            EditorTool.Pan or EditorTool.Water => null,
            EditorTool.Road => new BrushCursor(_vm.RoadWidth),
            EditorTool.Stamp => new BrushCursor(_vm.StampSize, Square: true, _vm.StampRotation),
            _ => new BrushCursor(_vm.BrushRadius * 2, _vm.IsSquareBrush, _vm.BrushRotation),
        };
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        // Leave typing in text boxes alone.
        if (e.Handled || Keyboard.FocusedElement is TextBox || Keyboard.Modifiers != ModifierKeys.None) return;

        if (_tools.IsDrawingRoad)
        {
            switch (e.Key)
            {
                case Key.Enter: _tools.FinishRoad(); e.Handled = true; return;
                case Key.Escape: _tools.CancelRoad(); e.Handled = true; return;
                case Key.Back: _tools.UndoRoadPoint(); e.Handled = true; return;
            }
        }

        var tool = _vm.Tools.FirstOrDefault(t => t.Shortcut == e.Key.ToString());
        if (tool is not null)
        {
            _vm.SelectedTool = tool.Tool;
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case Key.OemOpenBrackets: Resize(1 / 1.2); break;
            case Key.OemCloseBrackets: Resize(1.2); break;
            case Key.Q: Rotate(-15); break;
            case Key.E: Rotate(15); break;
            case Key.F12: SaveSnapshot(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Rotate(double degrees)
    {
        if (_vm.SelectedTool == EditorTool.Stamp) _vm.StampRotation = (_vm.StampRotation + degrees + 360) % 360;
        else if (_vm.HasRadius && _vm.IsSquareBrush) _vm.BrushRotation = (_vm.BrushRotation + degrees + 360) % 360;
    }

    private void Resize(double factor)
    {
        if (_vm.SelectedTool == EditorTool.Stamp) _vm.StampSize = Math.Clamp(_vm.StampSize * factor, 32, 4000);
        else _vm.BrushRadius = Math.Clamp(_vm.BrushRadius * factor, 2, 800);
    }

    private void Map_CursorMoved(object? sender, Point? map)
    {
        if (map is not { } m || _world is not { } world)
        {
            _vm.CursorText = "";
            Map.HoverPrefab = null;
            return;
        }
        int x = (int)Math.Floor(m.X);
        int z = world.Size - 1 - (int)Math.Floor(m.Y);
        var info = WorldQuery.At(world, x, z, _vm.Biomes, _vm.Prefabs);
        _vm.CursorText = info is null ? "" : MainViewModel.FormatPoint(info);
        Map.HoverPrefab = _vm.ShowPrefabs && _vm.SelectedTool == EditorTool.Pan ? info?.Prefab : null;
    }

    private void UpdateZoomText()
    {
        if (Map.Renderer is null) { _vm.ZoomText = ""; return; }
        double metresPerPixel = 1 / Map.Scale;
        _vm.ZoomText = metresPerPixel >= 1 ? $"{metresPerPixel:0.#} m/px" : $"{Map.Scale:0.#} px/m";
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Map.FitToView();

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>F12 saves what the window draws to %TEMP%\mapeditor-snapshot.png (a debugging aid).</summary>
    private void SaveSnapshot()
    {
        double dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var content = (FrameworkElement)Content;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            (int)(content.ActualWidth * dpi), (int)(content.ActualHeight * dpi), 96 * dpi, 96 * dpi, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        string path = Path.Combine(Path.GetTempPath(), "mapeditor-snapshot.png");
        using (var file = File.Create(path)) encoder.Save(file);
        _vm.Status = "Snapshot saved to " + path;
    }
}
