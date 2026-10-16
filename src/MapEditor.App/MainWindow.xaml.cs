using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Rendering;
using Microsoft.Win32;

namespace MapEditor.App;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand OpenWorldCommand = new("Open World", nameof(OpenWorldCommand), typeof(MainWindow));

    private readonly MainViewModel _vm = new();
    private readonly AppSettings _settings = AppSettings.Load();
    private Task _gameDataLoad = Task.CompletedTask;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        Overview.Target = Map;
        Map.CursorMoved += Map_CursorMoved;
        Map.ViewChanged += (_, _) => UpdateZoomText();
        _vm.PropertyChanged += Vm_PropertyChanged;
        Loaded += async (_, _) =>
        {
            _gameDataLoad = LoadGameDataAsync();
            // MapEditor.exe "<world folder>" opens that world straight away.
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1])) await OpenWorldAsync(args[1]);
        };
    }

    // ---- game data ---------------------------------------------------------

    private async Task LoadGameDataAsync()
    {
        if (_settings.GameFolder is not { } folder || !GameInstall.IsValid(folder))
        {
            _vm.GameFolderText = "Not set. Use Settings > Game Folder so biome names and prefab sizes are known.";
            _vm.Biomes = BiomeTable.Vanilla;
            _vm.Prefabs = PrefabLibrary.Empty;
            return;
        }

        _vm.GameFolderText = folder + "\n(loading prefabs...)";
        var sw = Stopwatch.StartNew();
        var (biomes, prefabs) = await Task.Run(() =>
        {
            var install = GameInstall.Open(folder);
            return (BiomeTable.Load(install), PrefabLibrary.Load(install));
        });
        _vm.Biomes = biomes;
        _vm.Prefabs = prefabs;
        Map.Prefabs = prefabs;
        Map.InvalidateVisual();
        _vm.GameFolderText = $"{folder}\n{prefabs.Count:N0} prefabs, {biomes.Biomes.Count} biomes ({sw.Elapsed.TotalSeconds:0.0} s)";
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
        if (Map.Renderer?.World is { } world) ShowWorld(world);
    }

    // ---- worlds ------------------------------------------------------------

    private async void OpenWorld_Executed(object sender, ExecutedRoutedEventArgs e)
    {
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
            ShowWorld(world);
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

    private void ShowWorld(World world)
    {
        var renderer = new MapRenderer(world, _vm.Biomes);
        Map.Prefabs = _vm.Prefabs;
        Map.Renderer = renderer;
        Overview.SetRenderer(renderer, _vm.Layers);
        _vm.WorldSummary = WorldSummaryFormatter.Format(world);
        Title = $"{world.Name} - 7D2D Map Editor";
        UpdateZoomText();
        Map.Focus();
    }

    // ---- view --------------------------------------------------------------

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Layers) && Map.Renderer is { } renderer)
            Overview.SetRenderer(renderer, _vm.Layers);
    }

    private void Map_CursorMoved(object? sender, Point? map)
    {
        if (map is not { } m || Map.Renderer?.World is not { } world)
        {
            _vm.CursorText = "";
            Map.HoverPrefab = null;
            return;
        }
        int x = (int)Math.Floor(m.X);
        int z = world.Size - 1 - (int)Math.Floor(m.Y);
        var info = WorldQuery.At(world, x, z, _vm.Biomes, _vm.Prefabs);
        _vm.CursorText = info is null ? "" : MainViewModel.FormatPoint(info);
        Map.HoverPrefab = _vm.ShowPrefabs ? info?.Prefab : null;
    }

    private void UpdateZoomText()
    {
        if (Map.Renderer is null) { _vm.ZoomText = ""; return; }
        double metresPerPixel = 1 / Map.Scale;
        _vm.ZoomText = metresPerPixel >= 1 ? $"{metresPerPixel:0.#} m/px" : $"{Map.Scale:0.#} px/m";
    }

    private void Fit_Click(object sender, RoutedEventArgs e) => Map.FitToView();

    /// <summary>F12 saves what the window draws to %TEMP%\mapeditor-snapshot.png (a debugging aid).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key != Key.F12) return;
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
        e.Handled = true;
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();
}
