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
using MapEditor.Core.Validate;
using Microsoft.Win32;

namespace MapEditor.App;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand NewWorldCommand = new("New World", nameof(NewWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenCommand = new("Open", nameof(OpenCommand), typeof(MainWindow));
    public static readonly RoutedUICommand OpenWorldCommand = new("Open World Folder", nameof(OpenWorldCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveCommand = new("Save", nameof(SaveCommand), typeof(MainWindow));
    public static readonly RoutedUICommand SaveAsCommand = new("Save Project As", nameof(SaveAsCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ExportCommand = new("Export World", nameof(ExportCommand), typeof(MainWindow));
    public static readonly RoutedUICommand ValidateCommand = new("Validate", nameof(ValidateCommand), typeof(MainWindow));
    public static readonly RoutedUICommand UndoCommand = new("Undo", nameof(UndoCommand), typeof(MainWindow));
    public static readonly RoutedUICommand RedoCommand = new("Redo", nameof(RedoCommand), typeof(MainWindow));

    private readonly MainViewModel _vm = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly UndoStack _undo = new();
    private readonly TerrainToolController _tools;
    private readonly PrefabToolController _prefabTools;
    private readonly TownToolController _townTools;
    private Task _gameDataLoad = Task.CompletedTask;
    private GameInstall? _install;

    private World? _world;
    private string? _sourcePath;  // world folder the world was opened from
    private string? _savePath;    // world folder Ctrl+S writes to (an opened world folder, or the last export)
    private string? _projectPath; // .7dmap the world was opened from or saved to; Ctrl+S prefers it
    private ProjectStore.ProjectSettings _projectSettings = new();
    private bool _dirty;
    private readonly System.Windows.Threading.DispatcherTimer _autosaveTimer = new() { Interval = TimeSpan.FromMinutes(5) };
    private bool _autosaving;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Overview.Target = Map;
        Map.CursorMoved += Map_CursorMoved;
        Map.ViewChanged += (_, _) => UpdateZoomText();
        _autosaveTimer.Tick += async (_, _) => await AutosaveAsync();
        _autosaveTimer.Start();
        _vm.PropertyChanged += Vm_PropertyChanged;

        _tools = new TerrainToolController(Map, _vm, _undo);
        _tools.Edited += (_, _) => OnWorldEdited();
        _prefabTools = new PrefabToolController(Map, _vm, _undo);
        _prefabTools.Edited += (_, _) => OnWorldEdited();
        _townTools = new TownToolController(Map, _vm, _undo);
        _townTools.Edited += (_, _) => OnWorldEdited();
        _undo.Changed += (_, _) => UpdateUndoText();

        Loaded += async (_, _) =>
        {
            _gameDataLoad = LoadGameDataAsync();
            _vm.Autosave = _settings.Autosave;
            // MapEditor.exe "<world folder or .7dmap>" opens it straight away.
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1])) await OpenWorldAsync(args[1]);
            else if (args.Length > 1 && File.Exists(args[1])) await OpenProjectAsync(args[1]);
            else await OfferRecoveryAsync();
            await CheckUpdatesQuietlyAsync();
        };
        Closing += MainWindow_Closing;

        // Open at the size it was closed at; fit a small screen.
        if (_settings.WindowWidth >= MinWidth && _settings.WindowHeight >= MinHeight)
        {
            Width = Math.Min(_settings.WindowWidth, SystemParameters.WorkArea.Width);
            Height = Math.Min(_settings.WindowHeight, SystemParameters.WorkArea.Height);
        }
        else
        {
            Width = Math.Min(Width, SystemParameters.WorkArea.Width);
            Height = Math.Min(Height, SystemParameters.WorkArea.Height);
        }
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        if (_settings.WindowMaximized) WindowState = WindowState.Maximized;
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
            LoadMixer();
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
        LoadMixer();
        Map.InvalidateVisual();
        _vm.GameFolderText = $"{folder}\n{prefabs.Count:N0} prefabs, {stamps.Names.Count} stamps, {biomes.Biomes.Count} biomes ({sw.Elapsed.TotalSeconds:0.0} s)";
    }

    /// <summary>Reads rwgmixer.xml for the open world's size, which sets the township tile ranges.</summary>
    private void LoadMixer()
    {
        try
        {
            _vm.Mixer = _install is null ? RwgMixer.Empty : RwgMixer.Load(_install, _world?.Size ?? 8192);
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            _vm.Mixer = RwgMixer.Empty;
            _vm.Status = "Could not read rwgmixer.xml, so the Town tool is unavailable: " + ex.Message;
        }
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

    private async void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        var dialog = new OpenFileDialog
        {
            Title = "Open a project or world",
            Filter = "Projects and worlds (*.7dmap, dtm.raw, map_info.xml)|*.7dmap;dtm.raw;map_info.xml|Projects (*.7dmap)|*.7dmap|All files (*.*)|*.*",
            InitialDirectory = _settings.LastWorldFolder is { } last && Directory.Exists(Path.GetDirectoryName(last))
                ? Path.GetDirectoryName(last)!
                : GameInstall.UserGeneratedWorldsDir,
        };
        if (dialog.ShowDialog(this) != true) return;
        if (dialog.FileName.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)) await OpenProjectAsync(dialog.FileName);
        else await OpenWorldAsync(Path.GetDirectoryName(dialog.FileName)!);
    }

    private async Task OpenProjectAsync(string path, bool recovered = false)
    {
        _vm.IsBusy = true;
        _vm.Status = "Loading " + Path.GetFileName(path) + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            ProjectStore.ProjectSettings settings = new();
            var world = await Task.Run(() => ProjectStore.Load(path, out settings));
            await _gameDataLoad;
            SetWorld(world, sourcePath: null, dirty: recovered);
            _projectPath = recovered ? null : path;
            _projectSettings = settings;
            _savePath = recovered ? null : settings.ExportFolder;
            UpdateTitle();
            _vm.Status = recovered
                ? $"Recovered {world.Name} from the autosave. Save it with Ctrl+S."
                : $"Opened {Path.GetFileName(path)} in {sw.Elapsed.TotalSeconds:0.0} s";
            if (!recovered)
            {
                _settings.LastWorldFolder = path;
                _settings.AddRecent(path);
                _settings.Save();
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException
                                       or System.Xml.XmlException)
        {
            _vm.Status = "Failed to open the project";
            MessageBox.Show(this, ex.Message, "Open", MessageBoxButton.OK, MessageBoxImage.Error);
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
            _settings.AddRecent(folder);
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
        _savePath = sourcePath;
        _projectPath = null;
        _projectSettings = new ProjectStore.ProjectSettings();
        _vm.Issues.Clear();
        _vm.IssuesText = "Press Validate (F7) to check the world.";
        _undo.Clear();
        _tools.CancelRoad();
        _tools.World = world;
        _prefabTools.World = world;
        _prefabTools.Refresh();
        _townTools.World = world;
        _townTools.Refresh();
        LoadMixer();
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
        _vm.WorldSizeText = $"{world.Size} × {world.Size} m";
        StartPanel.Visibility = Visibility.Collapsed;
        UpdateTitle();
        UpdateZoomText();
        UpdateToolState();
        Map.Focus();
    }

    // ---- save --------------------------------------------------------------

    private void HasWorld_CanExecute(object sender, CanExecuteRoutedEventArgs e) => e.CanExecute = _world is not null && !_vm.IsBusy;

    private async void Save_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async void Export_Executed(object sender, ExecutedRoutedEventArgs e) => await ExportAsync();

    /// <summary>
    /// Ctrl+S: a project saves to its .7dmap, a world opened from a folder saves back to that folder, and anything
    /// else asks where to save a project. Returns false if the user cancelled or the save failed.
    /// </summary>
    private async Task<bool> SaveAsync(bool saveAs)
    {
        if (_world is null) return false;
        if (!saveAs && _projectPath is null && _sourcePath is not null && _savePath is not null)
            return await WriteWorldAsync(_savePath);

        string? path = saveAs ? null : _projectPath;
        if (path is null)
        {
            var dialog = new SaveFileDialog
            {
                Title = "Save project",
                Filter = "Map editor project (*.7dmap)|*.7dmap",
                FileName = _world.Name + ProjectStore.Extension,
                InitialDirectory = Path.GetDirectoryName(_projectPath ?? _settings.LastWorldFolder ?? "") is { Length: > 0 } dir && Directory.Exists(dir)
                    ? dir
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (dialog.ShowDialog(this) != true) return false;
            path = dialog.FileName;
        }
        return await SaveProjectAsync(path);
    }

    private async Task<bool> SaveProjectAsync(string path)
    {
        if (_world is not { } world) return false;
        _vm.IsBusy = true;
        _vm.Status = "Saving " + Path.GetFileName(path) + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            world.Name = Path.GetFileNameWithoutExtension(path);
            var settings = _projectSettings;
            await Task.Run(() => ProjectStore.Save(world, path, settings));
            _projectPath = path;
            _dirty = false;
            DeleteAutosave();
            _settings.LastWorldFolder = path;
            _settings.AddRecent(path);
            _settings.Save();
            UpdateTitle();
            _vm.Status = $"Saved {Path.GetFileName(path)} in {sw.Elapsed.TotalSeconds:0.0} s. Use Export (Ctrl+E) to make a world the game can load.";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.Status = "Save failed";
            MessageBox.Show(this, ex.Message, "Save", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>Validates, then asks where to export the world folder.</summary>
    private async Task<bool> ExportAsync()
    {
        if (_world is not { } world) return false;
        var issues = await ValidateAsync(showPanel: false);
        string location = Path.GetDirectoryName(_savePath ?? _sourcePath ?? "") is { Length: > 0 } dir ? dir : GameInstall.UserGeneratedWorldsDir;
        string name = _savePath is { } saved ? Path.GetFileName(saved) : world.Name;
        var dialog = new ExportDialog(name, location, issues) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.Folder is null) return false;
        bool ok = await WriteWorldAsync(dialog.Folder);
        if (ok && _projectPath is not null)
        {
            // Remember the export in the project, so Export offers the same place next time.
            _projectSettings.ExportFolder = dialog.Folder;
            _dirty = true;
            UpdateTitle();
        }
        return ok;
    }

    /// <summary>Writes the world as a game world folder.</summary>
    private async Task<bool> WriteWorldAsync(string folder)
    {
        if (_world is not { } world) return false;
        bool newGuid = !string.Equals(Path.GetFullPath(folder), _sourcePath is null ? null : Path.GetFullPath(_sourcePath), StringComparison.OrdinalIgnoreCase)
                       && folder != _savePath;
        byte[]? template = null;
        if (!world.ExtraFiles.ContainsKey(WorldFiles.Level))
        {
            var found = LevelFile.FindTemplate(_install);
            if (found is null)
            {
                MessageBox.Show(this, "The world needs a main.ttw and none could be found to copy. Generate any world in the game once, then export again.",
                    "Export World", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            template = File.ReadAllBytes(found.Path);
        }

        var addedSpawn = SpawnHelper.EnsureOne(world);
        if (addedSpawn is not null) Map.InvalidateVisual();

        _vm.IsBusy = true;
        _vm.Status = "Writing " + folder + "...";
        try
        {
            var sw = Stopwatch.StartNew();
            string projectName = world.Name;
            world.Name = Path.GetFileName(folder);
            var options = new WorldWriteOptions { Overwrite = true, LevelTemplate = template, NewLevelGuid = newGuid };
            await Task.Run(() => WorldWriter.Write(world, folder, options));
            if (_projectPath is not null) world.Name = projectName;
            _savePath = folder;
            if (_projectPath is null)
            {
                // A world edited as a folder is now saved there.
                _sourcePath ??= folder;
                _dirty = false;
                DeleteAutosave();
            }
            _settings.LastWorldFolder = folder;
            _settings.Save();
            _vm.WorldSummary = WorldSummaryFormatter.Format(world);
            UpdateTitle();
            _vm.Status = $"Wrote {Path.GetFileName(folder)} in {sw.Elapsed.TotalSeconds:0.0} s. The game rebuilds its caches on first load."
                + (addedSpawn is null ? "" : $" Added a spawn point at {addedSpawn.X:0}, {addedSpawn.Z:0} because the world had none.");
            WarnAboutExistingGames(Path.GetFileName(folder));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _vm.Status = "Export failed";
            MessageBox.Show(this, ex.Message, "Export World", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    // ---- validation --------------------------------------------------------

    private async void Validate_Executed(object sender, ExecutedRoutedEventArgs e) => await ValidateAsync(showPanel: true);

    private async Task<IReadOnlyList<ValidationIssue>> ValidateAsync(bool showPanel)
    {
        if (_world is not { } world) return [];
        await _gameDataLoad;
        var library = _vm.Prefabs;
        bool template = world.ExtraFiles.ContainsKey(WorldFiles.Level) || LevelFile.FindTemplate(_install) is not null;
        _vm.IsBusy = true;
        _vm.Status = "Checking the world...";
        List<ValidationIssue> issues;
        try
        {
            // Edits can carry on while the checks run, so they get their own copy of the lists (the layers are only read).
            var view = new World
            {
                Name = world.Name, Size = world.Size, Height = world.Height, Biomes = world.Biomes, Roads = world.Roads,
                Water = world.Water, Radiation = world.Radiation,
            };
            view.Prefabs.AddRange(world.Prefabs.Select(p => p.Clone()));
            view.Spawns.AddRange(world.Spawns.Select(sp => sp.Clone()));
            foreach (var (k, v) in world.ExtraFiles) view.ExtraFiles[k] = v;
            foreach (var (k, v) in world.PassthroughImages) view.PassthroughImages[k] = v;
            issues = await Task.Run(() => WorldValidator.Validate(view, library, template));
        }
        finally
        {
            _vm.IsBusy = false;
        }
        _vm.Issues.Clear();
        foreach (var issue in issues) _vm.Issues.Add(issue);
        int errors = issues.Count(i => i.Severity == IssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == IssueSeverity.Warning);
        static string Count(int n, string word) => n == 1 ? $"1 {word}" : $"{n} {word}s";
        _vm.IssuesText = issues.Count == 0 ? "No problems found." : $"{Count(errors, "error")}, {Count(warnings, "warning")}.";
        _vm.Status = "Validated: " + _vm.IssuesText;
        if (showPanel)
        {
            _vm.SidePanelTab = 1;
            _vm.ShowSidePanel = true;
        }
        return issues;
    }

    private void Issue_DoubleClick(object sender, MouseButtonEventArgs e) => JumpToIssue();

    private void Issue_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        JumpToIssue();
        e.Handled = true;
    }

    /// <summary>Centres the map on the selected issue and zooms in to about a street tile across.</summary>
    private void JumpToIssue()
    {
        if (IssuesList.SelectedItem is not ValidationIssue { HasLocation: true } issue || _world is not { } world) return;
        double target = Math.Min(Map.ActualWidth, Map.ActualHeight) / 300;
        if (Map.Scale < target) Map.ZoomTo(target);
        Map.CenterOn(new Point(issue.X!.Value + 0.5, world.Size - issue.Z!.Value - 0.5));
    }

    // ---- autosave ----------------------------------------------------------

    private static readonly string AutosaveDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "7D2DMapEditor", "autosave");

    private static string AutosavePath => Path.Combine(AutosaveDir, "autosave" + ProjectStore.Extension);

    /// <summary>
    /// Every few minutes, writes unsaved work to an autosave project. A snapshot is taken on the UI thread, so
    /// editing carries on while it is written. The autosave is deleted after a normal save or exit.
    /// </summary>
    private async Task AutosaveAsync()
    {
        if (!_vm.Autosave || _autosaving || !_dirty || _world is not { } world || _vm.IsBusy
            || _tools.IsStroking || _prefabTools.IsDragging || _townTools.IsDragging)
            return;
        _autosaving = true;
        try
        {
            var snapshot = world.Clone();
            await Task.Run(() => ProjectStore.Save(snapshot, AutosavePath));
            _vm.Status = $"Autosaved at {DateTime.Now:t}.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm.Status = "Autosave failed: " + ex.Message;
        }
        finally
        {
            _autosaving = false;
        }
    }

    private static void DeleteAutosave()
    {
        try
        {
            if (File.Exists(AutosavePath)) File.Delete(AutosavePath);
        }
        catch (IOException)
        {
            // Best effort; it is offered again at the next start.
        }
    }

    /// <summary>An autosave left at start-up means the editor did not close normally; offer to bring the work back.</summary>
    private async Task OfferRecoveryAsync()
    {
        if (!File.Exists(AutosavePath)) return;
        // Let the window draw first, so the question doesn't sit on a blank window.
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ContextIdle);
        var when = File.GetLastWriteTime(AutosavePath);
        var answer = MessageBox.Show(this,
            $"The editor did not close normally last time. Recover the work autosaved at {when:g}?",
            "Recover unsaved work", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Yes) await OpenProjectAsync(AutosavePath, recovered: true);
        else DeleteAutosave();
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
        if (_closeConfirmed || _world is null || !_dirty)
        {
            DeleteAutosave();
            _settings.Autosave = _vm.Autosave;
            _settings.WindowMaximized = WindowState == WindowState.Maximized;
            _settings.WindowWidth = RestoreBounds.Width;
            _settings.WindowHeight = RestoreBounds.Height;
            _settings.Save();
            return;
        }
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

    private void Undo_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _undo.CanUndo && !_tools.IsStroking && !_prefabTools.IsDragging && !_townTools.IsDragging && !_vm.IsBusy;

    private void Redo_CanExecute(object sender, CanExecuteRoutedEventArgs e) =>
        e.CanExecute = _undo.CanRedo && !_tools.IsStroking && !_prefabTools.IsDragging && !_townTools.IsDragging && !_vm.IsBusy;

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
        _prefabTools.Refresh();
        _townTools.Refresh();
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

    // ---- palette, panels and help --------------------------------------------

    /// <summary>A biome swatch picks that biome and the biome brush, as clicking a colour in Paint picks it.</summary>
    private void BiomeSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not BiomeDefinition biome) return;
        _vm.SelectedBiome = biome;
        _vm.SelectedTool = EditorTool.Biome;
    }

    private void RoadSwatch_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not Swatch { Road: { } road }) return;
        _vm.RoadType = road;
        _vm.SelectedTool = EditorTool.Road;
    }

    private void LayersPopup_Closed(object? sender, EventArgs e) => LayersButton.IsChecked = false;

    private void ShowPrefabBrowser_Click(object sender, RoutedEventArgs e)
    {
        _vm.SidePanelTab = 0;
        _vm.ShowSidePanel = true;
    }

    private void CloseSidePanel_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowSidePanel = false;
        Map.Focus();
    }

    private void WorldInfo_Click(object sender, RoutedEventArgs e)
    {
        _vm.SidePanelTab = 2;
        _vm.ShowSidePanel = true;
    }

    private void Shortcuts_Click(object sender, RoutedEventArgs e) => ShowShortcuts();

    private ShortcutsWindow? _shortcuts;

    private void ShowShortcuts()
    {
        if (_shortcuts is { } open)
        {
            open.Activate();
            return;
        }
        _shortcuts = new ShortcutsWindow(_vm.Tools) { Owner = this };
        _shortcuts.Closed += (_, _) => _shortcuts = null;
        _shortcuts.Show();
    }

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        var result = await Updates.CheckAsync(_settings);
        if (result.Update is null)
        {
            MessageBox.Show(this, result.Message, "Check for Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, result.Message + "\n\nInstall it now? The editor restarts.", "Check for Updates",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;
        if (!await ConfirmDiscardAsync()) return;
        _vm.Status = "Downloading the update...";
        _vm.IsBusy = true;
        try
        {
            _closeConfirmed = true;
            DeleteAutosave();
            await Updates.ApplyAsync(result);
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or IOException)
        {
            _closeConfirmed = false;
            _vm.Status = "Update failed: " + ex.Message;
        }
        finally
        {
            _vm.IsBusy = false;
        }
    }

    /// <summary>A quiet check at start-up; only says something when there is an update.</summary>
    private async Task CheckUpdatesQuietlyAsync()
    {
        var result = await Updates.CheckAsync(_settings);
        if (result.Update is not null) _vm.Status = result.Message + " Use Help > Check for Updates to install it.";
    }

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var version = typeof(MainWindow).Assembly.GetName().Version;
        MessageBox.Show(this,
            $"7D2D Map Editor {version?.ToString(3)}\n\nMakes and edits 7 Days to Die worlds. Game assets are read from your own install " +
            "and never shipped with the editor.\n\nNot made by or affiliated with The Fun Pimps.",
            "About", MessageBoxButton.OK, MessageBoxImage.Information);
    }


    // ---- tools and view ----------------------------------------------------

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.IsBusy):
                // Commands that check IsBusy are not re-queried by WPF on their own.
                CommandManager.InvalidateRequerySuggested();
                break;
            case nameof(MainViewModel.Layers) when Map.Renderer is { } renderer:
                Overview.SetRenderer(renderer, _vm.Layers);
                break;
            case nameof(MainViewModel.SelectedTool):
                if (_vm.IsPrefabTool)
                {
                    _vm.SidePanelTab = 0;
                    _vm.ShowSidePanel = true;
                }
                if (!_vm.IsRoadTool) _tools.CancelRoad();
                _prefabTools.Refresh();
                _townTools.Refresh();
                UpdateToolState();
                break;
            case nameof(MainViewModel.SelectedPrefabInfo):
            case nameof(MainViewModel.PrefabRotation):
                _prefabTools.UpdateGhost();
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
        _vm.SelectionSizeText = _vm.HasSize ? _vm.SizeText : "";
        Map.ToolActive = _vm.SelectedTool != EditorTool.Pan;
        Map.ShowLots = _vm.IsPrefabTool;
        if (!_vm.IsPrefabTool) Map.HoverPrefab = null;
        Map.BrushCursor = _vm.SelectedTool switch
        {
            EditorTool.Pan or EditorTool.Water or EditorTool.Prefab or EditorTool.Spawn or EditorTool.Town => null,
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

        if (_vm.IsTownTool && e.Key == Key.Escape && _townTools.Cancel()) { e.Handled = true; return; }
        if (_vm.IsTownTool && e.Key == Key.Enter && _townTools.Accept()) { e.Handled = true; return; }
        if (e.Key == Key.Escape && _prefabTools.Cancel()) { e.Handled = true; return; }
        if (e.Key == Key.Delete && (_vm.IsPrefabTool || _vm.IsSpawnTool)) { _prefabTools.DeleteSelected(); e.Handled = true; return; }
        if (e.Key is Key.PageUp or Key.PageDown && _vm.IsPrefabTool)
        {
            _prefabTools.NudgeHeight(e.Key == Key.PageUp ? 1 : -1);
            e.Handled = true;
            return;
        }

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
            case Key.F1: ShowShortcuts(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void Rotate(double degrees)
    {
        // Prefabs turn a quarter at a time; positive quarter turns are counter-clockwise, so Q (-) gives +1.
        if (_vm.IsPrefabTool) _prefabTools.Rotate(degrees < 0 ? 1 : -1);
        else if (_vm.IsSpawnTool) _prefabTools.RotateSpawn(degrees < 0 ? -45 : 45);
        else if (_vm.SelectedTool == EditorTool.Stamp) _vm.StampRotation = (_vm.StampRotation + degrees + 360) % 360;
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
        Map.HoverPrefab = _vm.ShowPrefabs && (_vm.SelectedTool == EditorTool.Pan || _vm.IsPrefabTool && _vm.SelectedPrefabInfo is null)
            ? info?.Prefab : null;
    }

    private bool _syncingZoom;

    /// <summary>Zoom is shown as a percentage: 100% is one screen pixel (DIP) per metre.</summary>
    private void UpdateZoomText()
    {
        if (Map.Renderer is null)
        {
            _vm.ZoomPercentText = "";
            return;
        }
        double percent = Map.Scale * 100;
        _vm.ZoomPercentText = percent >= 10 ? $"{percent:0}%" : $"{percent:0.#}%";
        _syncingZoom = true;
        _vm.ZoomLog = Math.Log2(Map.Scale);
        _syncingZoom = false;
    }

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingZoom || Map.Renderer is null) return;
        Map.ZoomTo(Math.Pow(2, e.NewValue));
    }

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1.25);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Map.ZoomBy(1 / 1.25);

    private void ZoomMenu_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ZoomMenuButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Top };
        var fit = new MenuItem { Header = "Fit to window" };
        fit.Click += (_, _) => Map.FitToView();
        menu.Items.Add(fit);
        menu.Items.Add(new Separator());
        foreach (double percent in new double[] { 1600, 800, 400, 200, 100, 50, 25, 10, 5, 2 })
        {
            var item = new MenuItem { Header = $"{percent}%", IsChecked = Math.Abs(Map.Scale * 100 - percent) < 0.5 };
            item.Click += (_, _) => Map.ZoomTo(percent / 100);
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Map.FitToView();


    // ---- prefab info card --------------------------------------------------

    private void TownReroll_Click(object sender, RoutedEventArgs e) => WithMapFocus(_townTools.Reroll);
    private void TownBuild_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _townTools.Accept());
    private void TownCancel_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _townTools.Cancel());

    private void PrefabCardClose_Click(object sender, RoutedEventArgs e) => _prefabTools.Deselect();
    private void PrefabTurnLeft_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.Rotate(1));
    private void PrefabTurnRight_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.Rotate(-1));
    private void PrefabRaise_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.NudgeHeight(1));
    private void PrefabLower_Click(object sender, RoutedEventArgs e) => WithMapFocus(() => _prefabTools.NudgeHeight(-1));
    private void PrefabReplace_Click(object sender, RoutedEventArgs e) => WithMapFocus(_prefabTools.ReplaceSelected);
    private void PrefabDelete_Click(object sender, RoutedEventArgs e) => WithMapFocus(_prefabTools.DeleteSelected);

    /// <summary>Card buttons act on the map selection even while a prefab is picked for placing.</summary>
    private void WithMapFocus(Action action)
    {
        action();
        Map.Focus();
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>Refills File > Open Recent each time the File menu opens.</summary>
    private void FileMenu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource != sender) return;
        RecentMenu.Items.Clear();
        for (int i = 0; i < _settings.RecentFiles.Count; i++)
        {
            string path = _settings.RecentFiles[i];
            bool isProject = path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
            string name = isProject ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path.TrimEnd('\\', '/')) + " (world folder)";
            // Underscores in names are doubled so they aren't taken as access keys.
            string accessKey = i < 9 ? $"_{i + 1} " : "";
            var item = new MenuItem { Header = accessKey + name.Replace("_", "__"), ToolTip = path, Tag = path };
            item.Click += RecentItem_Click;
            RecentMenu.Items.Add(item);
        }
        if (RecentMenu.Items.Count > 0)
        {
            RecentMenu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "_Clear Recent List" };
            clear.Click += (_, _) =>
            {
                _settings.RecentFiles.Clear();
                _settings.Save();
            };
            RecentMenu.Items.Add(clear);
        }
        RecentMenu.IsEnabled = RecentMenu.Items.Count > 0;
    }

    private async void RecentItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string path } || _vm.IsBusy) return;
        bool isProject = path.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase);
        if (isProject ? !File.Exists(path) : !Directory.Exists(path))
        {
            if (MessageBox.Show(this, $"{path}\n\nThis can't be found. It may have been moved, renamed or deleted. Remove it from the list?",
                    "Open Recent", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                _settings.RecentFiles.Remove(path);
                _settings.Save();
            }
            return;
        }
        if (!await ConfirmDiscardAsync()) return;
        if (isProject) await OpenProjectAsync(path);
        else await OpenWorldAsync(path);
    }

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
