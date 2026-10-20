using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;

namespace MapEditor.App.Map;

/// <summary>
/// Pan/zoom view of a world. Raster layers come from a tiled mip pyramid rendered on background
/// threads; prefabs and spawns are drawn as vectors on top.
/// "Map coordinates" here are metres from the north-west corner: u east, v south.
/// </summary>
public sealed class MapView : FrameworkElement
{
    private const double MaxScale = 16; // DIPs per metre
    private const double ZoomStep = 1.25;

    private readonly TileCache _cache = new(768);
    private readonly ConcurrentStack<(TileKey Key, int Epoch)> _queue = new();
    private readonly ConcurrentDictionary<(TileKey Key, int Epoch), byte> _pending = new();
    // Bumped for every tile an edit touches; cached tiles with an older epoch are redrawn.
    private readonly ConcurrentDictionary<(int Level, int X, int Y), int> _epochs = new();
    private volatile HashSet<TileKey> _wanted = [];
    private int _workers;
    private int _generation;
    private int _invalidateQueued;

    private MapRenderer? _renderer;
    private double _scale = 1;
    private Point _origin; // map coords at the control's top-left
    private bool _needsFit;
    private bool _fitted; // the view has not moved since FitToView
    private Point? _dragStart;
    private Point _dragOrigin;
    private bool _toolDrag;
    private Point? _mouseMap;

    // Prefab outlines are drawn in software (see OverlayRaster); colours are premultiplied BGRA.
    // Every outline gets a dark halo and every footprint a light tint, so they read on any biome colour.
    private static readonly uint PrefabColour = OverlayRaster.Premultiply(Color.FromArgb(255, 255, 170, 40));
    private static readonly uint PrefabTint = OverlayRaster.Premultiply(Color.FromArgb(70, 255, 170, 40));
    private static readonly uint PartColour = OverlayRaster.Premultiply(Color.FromArgb(200, 255, 230, 150));
    private static readonly uint StreetTileColour = OverlayRaster.Premultiply(Color.FromArgb(230, 235, 235, 245));
    private static readonly uint StreetTileTint = OverlayRaster.Premultiply(Color.FromArgb(45, 20, 20, 30));
    private static readonly uint TraderColour = OverlayRaster.Premultiply(Color.FromArgb(255, 235, 80, 255));
    private static readonly uint TraderTint = OverlayRaster.Premultiply(Color.FromArgb(110, 235, 80, 255));
    private static readonly uint HaloColour = OverlayRaster.Premultiply(Color.FromArgb(170, 0, 0, 0));
    private static readonly Brush DimBrush = Frozen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)));
    private readonly OverlayRaster _overlay = new();
    private readonly OverlayRaster _townOverlay = new(); // its own bitmap: sharing one would wipe the prefab layer
    private static readonly uint LotColour = OverlayRaster.Premultiply(Color.FromArgb(150, 120, 200, 255));
    private static readonly Pen HoverPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Pen SelectedPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(80, 220, 255)), 2.5));
    private static readonly Brush SelectedFill = Frozen(new SolidColorBrush(Color.FromArgb(40, 80, 220, 255)));
    private static readonly Pen GhostPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(120, 255, 140)), 2));
    private static readonly Brush GhostFill = Frozen(new SolidColorBrush(Color.FromArgb(60, 120, 255, 140)));
    private static readonly Pen GhostWarnPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(255, 90, 80)), 2));
    private static readonly Brush GhostWarnFill = Frozen(new SolidColorBrush(Color.FromArgb(60, 255, 90, 80)));
    private static readonly Pen LotPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(120, 200, 255)), 2) { DashStyle = DashStyles.Dash });
    private static readonly Brush SelectedSpawnFill = Frozen(new SolidColorBrush(Color.FromRgb(80, 220, 255)));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
    private static readonly Brush SpawnFill = Frozen(new SolidColorBrush(Color.FromRgb(60, 220, 90)));
    private static readonly Pen SpawnPen = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Pen SpawnFacingPen = Frozen(new Pen(Brushes.White, 2.5) { EndLineCap = PenLineCap.Triangle });
    private static readonly Pen EdgeShadowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(70, 0, 0, 0)), 6));
    private static readonly Pen EdgePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(120, 128, 128, 128)), 1));
    private static readonly Pen CursorPen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen CursorShadowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 3.5));

    public MapView()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SizeChanged += (_, e) =>
        {
            // A view still showing the whole world refits; otherwise the point in the middle stays in the middle.
            if (_needsFit || _fitted) FitToView();
            else if (e.PreviousSize.Width > 0)
                _origin = new Point(_origin.X + (e.PreviousSize.Width - e.NewSize.Width) / 2 / _scale,
                    _origin.Y + (e.PreviousSize.Height - e.NewSize.Height) / 2 / _scale);
            OnViewChanged();
        };
    }

    public event EventHandler? ViewChanged;

    /// <summary>What shows around the world (the theme's canvas backdrop).</summary>
    public Brush Backdrop
    {
        get => (Brush)GetValue(BackdropProperty);
        set => SetValue(BackdropProperty, value);
    }

    public static readonly DependencyProperty BackdropProperty = DependencyProperty.Register(
        nameof(Backdrop), typeof(Brush), typeof(MapView),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Raised with the map coordinate under the mouse, or null when it leaves.</summary>
    public event EventHandler<Point?>? CursorMoved;

    public MapRenderer? Renderer
    {
        get => _renderer;
        set
        {
            _renderer = value;
            ResetTiles();
            _needsFit = true;
            if (ActualWidth > 0) FitToView();
            InvalidateVisual();
        }
    }

    private PrefabLibrary _prefabs = PrefabLibrary.Empty;
    private PrefabDetailCache _detail = new(PrefabLibrary.Empty);

    public PrefabLibrary Prefabs
    {
        get => _prefabs;
        set
        {
            if (ReferenceEquals(_prefabs, value)) return;
            _prefabs = value;
            _detail = new PrefabDetailCache(value);
            _detail.Built += QueueInvalidate;
            InvalidateVisual();
        }
    }

    /// <summary>When true, left-button drags go to <see cref="ToolInput"/>; the right button still pans.</summary>
    public bool ToolActive { get; set; }

    /// <summary>Left-button presses, drags and releases while <see cref="ToolActive"/> is set, in map coordinates.</summary>
    public event EventHandler<ToolInputEventArgs>? ToolInput;

    private BrushCursor? _brushCursor;

    /// <summary>Outline drawn under the mouse to show what a tool will affect.</summary>
    public BrushCursor? BrushCursor
    {
        get => _brushCursor;
        set
        {
            _brushCursor = value;
            InvalidateVisual();
        }
    }

    private RoadPreview? _roadPreview;

    /// <summary>The road being drawn, shown with a rubber band to the mouse.</summary>
    /// <summary>
    /// With no tool active (the Select tool), whether a left press at a map point grabs something there: if so it goes
    /// to the tool as a drag, else the map pans.
    /// </summary>
    public Func<Point, bool>? GrabTest { get; set; }

    /// <summary>True while a right drag (or a left drag on empty ground with the Select tool) is panning the map.</summary>
    public bool IsPanning => _dragStart is not null;

    public RoadPreview? RoadPreview
    {
        get => _roadPreview;
        set
        {
            _roadPreview = value;
            InvalidateVisual();
        }
    }

    private Func<Point, RoadSnap?>? _roadSnapper;

    /// <summary>Snaps the mouse for the road tool: the rubber band runs to the snapped point, and road ends get a ring.</summary>
    public Func<Point, RoadSnap?>? RoadSnapper
    {
        get => _roadSnapper;
        set
        {
            _roadSnapper = value;
            InvalidateVisual();
        }
    }

    public MapLayers Layers
    {
        get => (MapLayers)GetValue(LayersProperty);
        set => SetValue(LayersProperty, value);
    }

    public static readonly DependencyProperty LayersProperty = DependencyProperty.Register(
        nameof(Layers), typeof(MapLayers), typeof(MapView),
        new FrameworkPropertyMetadata(MapLayers.All, (d, e) =>
        {
            var view = (MapView)d;
            view._layers = (int)(MapLayers)e.NewValue;
            view.ResetTiles();
        }));

    // Copy of Layers that worker threads can read.
    private volatile int _layers = (int)MapLayers.All;

    public bool ShowPrefabs
    {
        get => (bool)GetValue(ShowPrefabsProperty);
        set => SetValue(ShowPrefabsProperty, value);
    }

    public static readonly DependencyProperty ShowPrefabsProperty = DependencyProperty.Register(
        nameof(ShowPrefabs), typeof(bool), typeof(MapView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Draws prefabs from above (roads, pavements, buildings) once they are big enough on screen.</summary>
    public bool ShowPrefabDetail
    {
        get => (bool)GetValue(ShowPrefabDetailProperty);
        set => SetValue(ShowPrefabDetailProperty, value);
    }

    public static readonly DependencyProperty ShowPrefabDetailProperty = DependencyProperty.Register(
        nameof(ShowPrefabDetail), typeof(bool), typeof(MapView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Darkens the map under the prefab and spawn overlays so they stand out.</summary>
    public bool DimMap
    {
        get => (bool)GetValue(DimMapProperty);
        set => SetValue(DimMapProperty, value);
    }

    public static readonly DependencyProperty DimMapProperty = DependencyProperty.Register(
        nameof(DimMap), typeof(bool), typeof(MapView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool ShowSpawns
    {
        get => (bool)GetValue(ShowSpawnsProperty);
        set => SetValue(ShowSpawnsProperty, value);
    }

    public static readonly DependencyProperty ShowSpawnsProperty = DependencyProperty.Register(
        nameof(ShowSpawns), typeof(bool), typeof(MapView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    private PrefabGhost? _prefabGhost;

    /// <summary>The prefab the Prefabs tool is about to place or drop.</summary>
    public PrefabGhost? PrefabGhost
    {
        get => _prefabGhost;
        set
        {
            _prefabGhost = value;
            InvalidateVisual();
        }
    }

    private Point? _spawnGhost;

    /// <summary>Where the Spawn tool would add a spawn point (map pixels), drawn see-through under the cursor.</summary>
    public Point? SpawnGhost
    {
        get => _spawnGhost;
        set
        {
            if (_spawnGhost == value) return;
            _spawnGhost = value;
            InvalidateVisual();
        }
    }

    private TownPreview? _townPreview;

    /// <summary>The Town tool's selection and planned town.</summary>
    public TownPreview? TownPreview
    {
        get => _townPreview;
        set
        {
            _townPreview = value;
            InvalidateVisual();
        }
    }

    private PrefabInstance? _selectedPrefab;

    public PrefabInstance? SelectedPrefab
    {
        get => _selectedPrefab;
        set
        {
            _selectedPrefab = value;
            InvalidateVisual();
        }
    }

    private SpawnPoint? _selectedSpawn;

    public SpawnPoint? SelectedSpawn
    {
        get => _selectedSpawn;
        set
        {
            _selectedSpawn = value;
            InvalidateVisual();
        }
    }

    private bool _showLots;

    /// <summary>Outlines the POI lots of street tiles, so POIs can be dropped into them.</summary>
    public bool ShowLots
    {
        get => _showLots;
        set
        {
            _showLots = value;
            InvalidateVisual();
        }
    }

    private PrefabInstance? _hoverPrefab;

    public PrefabInstance? HoverPrefab
    {
        get => _hoverPrefab;
        set
        {
            if (ReferenceEquals(_hoverPrefab, value)) return;
            _hoverPrefab = value;
            InvalidateVisual();
        }
    }

    /// <summary>DIPs per metre.</summary>
    public double Scale => _scale;

    /// <summary>The part of the map currently on screen, in map coordinates.</summary>
    public Rect VisibleMapRect => new(_origin, new Size(ActualWidth / _scale, ActualHeight / _scale));

    public Point ScreenToMap(Point p) => new(_origin.X + p.X / _scale, _origin.Y + p.Y / _scale);

    public Point MapToScreen(Point m) => new((m.X - _origin.X) * _scale, (m.Y - _origin.Y) * _scale);

    public void FitToView()
    {
        if (_renderer is null || ActualWidth <= 0 || ActualHeight <= 0) return;
        _needsFit = false;
        int size = _renderer.World.Size;
        SetScale(Math.Min(ActualWidth, ActualHeight) / size * 0.95);
        CenterOn(new Point(size / 2.0, size / 2.0));
        _fitted = true;
    }

    public void CenterOn(Point map)
    {
        _origin = new Point(map.X - ActualWidth / 2 / _scale, map.Y - ActualHeight / 2 / _scale);
        _fitted = false;
        OnViewChanged();
    }

    /// <summary>Zooms about the middle of the view to <paramref name="scale"/> DIPs per metre (the zoom slider).</summary>
    public void ZoomTo(double scale) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), scale / _scale);

    public void ZoomBy(double factor) => ZoomAt(new Point(ActualWidth / 2, ActualHeight / 2), factor);

    public static double MaxZoom => MaxScale;

    private double MinScale => _renderer is null ? 0.01 : Math.Min(ActualWidth, ActualHeight) / _renderer.World.Size / 4;

    private void SetScale(double scale)
    {
        _scale = Math.Clamp(scale, Math.Max(MinScale, 1e-4), MaxScale);
        // Show crisp metre-sized pixels when zoomed in, smooth filtering when zoomed out.
        RenderOptions.SetBitmapScalingMode(this, _scale * PixelsPerDip >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.Linear);
    }

    private void ZoomAt(Point screen, double factor)
    {
        var anchor = ScreenToMap(screen);
        SetScale(_scale * factor);
        _origin = new Point(anchor.X - screen.X / _scale, anchor.Y - screen.Y / _scale);
        _fitted = false;
        OnViewChanged();
    }

    private void OnViewChanged()
    {
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private double PixelsPerDip => VisualTreeHelper.GetDpi(this).PixelsPerDip;

    // ---- input -------------------------------------------------------------

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(this), Math.Pow(ZoomStep, e.Delta / 120.0));
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        Focus();
        // One drag at a time: a second button pressed mid-drag is ignored.
        if (_toolDrag || _dragStart is not null)
        {
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == MouseButton.Left && _renderer is not null && (ToolActive || GrabTest?.Invoke(ScreenToMap(e.GetPosition(this))) == true))
        {
            _toolDrag = true;
            CaptureMouse();
            ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Down, ScreenToMap(e.GetPosition(this)), Keyboard.Modifiers, e.ClickCount));
            e.Handled = true;
            return;
        }
        // A right drag pans with any tool, as in image editors; a left drag pans with the Pan tool. The middle
        // button is left free for now.
        if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            _panButton = e.ChangedButton;
            _dragStart = e.GetPosition(this);
            _dragOrigin = _origin;
            CaptureMouse();
            Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        if (_toolDrag && e.ChangedButton == MouseButton.Left)
        {
            _toolDrag = false;
            ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Up, ScreenToMap(e.GetPosition(this)), Keyboard.Modifiers));
            ReleaseMouseCapture();
            return;
        }
        if (_dragStart is null || e.ChangedButton != _panButton) return;
        _dragStart = null;
        ReleaseMouseCapture();
        Cursor = null;
        e.Handled = true;
    }

    private MouseButton _panButton;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragStart is { } start)
        {
            _origin = new Point(_dragOrigin.X - (p.X - start.X) / _scale, _dragOrigin.Y - (p.Y - start.Y) / _scale);
            _fitted = false;
            OnViewChanged();
        }
        var map = ScreenToMap(p);
        if (_toolDrag) ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Move, map, Keyboard.Modifiers));
        _mouseMap = map;
        if (_brushCursor is not null || _roadPreview is not null || _roadSnapper is not null || _prefabGhost is not null) InvalidateVisual();
        CursorMoved?.Invoke(this, map);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        // Losing capture mid-stroke (for example on alt-tab) still has to finish the stroke.
        if (_toolDrag)
        {
            _toolDrag = false;
            ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Up, _mouseMap ?? default, Keyboard.Modifiers));
        }
        // And a pan, so the next press isn't taken for a second button mid-drag.
        if (_dragStart is not null)
        {
            _dragStart = null;
            Cursor = null;
        }
        base.OnLostMouseCapture(e);
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        _mouseMap = null;
        if (_brushCursor is not null) InvalidateVisual();
        CursorMoved?.Invoke(this, null);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var centre = new Point(ActualWidth / 2, ActualHeight / 2);
        switch (e.Key)
        {
            case Key.Home: FitToView(); break;
            case Key.OemPlus or Key.Add: ZoomAt(centre, ZoomStep); break;
            case Key.OemMinus or Key.Subtract: ZoomAt(centre, 1 / ZoomStep); break;
            default: return;
        }
        e.Handled = true;
    }

    // ---- rendering ---------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Backdrop, null, new Rect(RenderSize));
        if (_renderer is null) return;

        // The world sits on the backdrop like a page in Paint: a soft shadow and a thin edge.
        var world = new Rect(MapToScreen(new Point(0, 0)), MapToScreen(new Point(_renderer.World.Size, _renderer.World.Size)));
        dc.DrawRectangle(null, EdgeShadowPen, new Rect(world.X + 2, world.Y + 2, world.Width, world.Height));
        dc.DrawRectangle(null, EdgePen, Rect.Inflate(world, 0.5, 0.5));

        double dpi = PixelsPerDip;
        double metresPerDevicePixel = 1 / (_scale * dpi);
        int top = _renderer.Levels - 1;
        int level = Math.Clamp((int)Math.Floor(Math.Log2(Math.Max(metresPerDevicePixel, 1))), 0, top);
        int gen = _generation;

        var visible = VisibleMapRect;
        var wanted = new HashSet<TileKey>();
        var topKey = new TileKey(top, 0, 0, gen);
        wanted.Add(topKey);

        int tileMetres = MapRenderer.TileSize << level;
        int across = _renderer.TilesAcross(level);
        int tx0 = Math.Max(0, (int)Math.Floor(visible.Left / tileMetres));
        int ty0 = Math.Max(0, (int)Math.Floor(visible.Top / tileMetres));
        int tx1 = Math.Min(across - 1, (int)Math.Floor(visible.Right / tileMetres));
        int ty1 = Math.Min(across - 1, (int)Math.Floor(visible.Bottom / tileMetres));

        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
                wanted.Add(new TileKey(level, tx, ty, gen));
        _wanted = wanted;

        // Request the top tile first so there is always something to fall back to.
        if (!_cache.TryGet(topKey, out _, out int topEpoch) || topEpoch != Epoch(topKey)) Request(topKey);
        for (int ty = ty0; ty <= ty1; ty++)
            for (int tx = tx0; tx <= tx1; tx++)
            {
                var key = new TileKey(level, tx, ty, gen);
                var rect = TileRect(level, tx, ty, dpi);
                if (_cache.TryGet(key, out var bitmap, out int epoch))
                {
                    // A stale tile is still drawn until its replacement is ready, so edits do not flicker.
                    dc.DrawImage(bitmap, rect);
                    if (epoch != Epoch(key)) Request(key);
                    continue;
                }
                Request(key);
                DrawFallback(dc, key, rect, top, dpi);
            }

        if (DimMap) dc.DrawRectangle(DimBrush, null, new Rect(RenderSize));
        if (ShowPrefabs) DrawPrefabs(dc, visible);
        if (ShowSpawns) DrawSpawns(dc, visible);
        DrawTownPreview(dc);
        DrawPrefabGhost(dc);
        DrawSpawnGhost(dc);
        DrawRoadPreview(dc);
        DrawBrushCursor(dc);
    }

    private void DrawRoadPreview(DrawingContext dc)
    {
        var snap = _mouseMap is { } mouse && _roadSnapper is { } snapper ? snapper(mouse) : null;
        if (_roadPreview is not { MapPoints.Count: > 0 } road)
        {
            if (snap is { OnEnd: true }) DrawRoadEndMark(dc, MapToScreen(snap.MapPoint));
            return;
        }
        var points = road.MapPoints.Select(MapToScreen).ToList();
        if (_mouseMap is { } m) points.Add(MapToScreen(snap?.MapPoint ?? m));

        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], false, false);
            g.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        geometry.Freeze();
        var surface = new Pen(road.Asphalt ? RoadAsphaltBrush : RoadGravelBrush, Math.Max(3, road.WidthMetres * _scale))
        {
            StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
        };
        dc.DrawGeometry(null, surface, geometry);
        dc.DrawGeometry(null, CursorPen, geometry);
        foreach (var p in points.Take(road.MapPoints.Count))
            dc.DrawEllipse(Brushes.White, CursorShadowPen, p, 3, 3);
        if (snap is { OnEnd: true }) DrawRoadEndMark(dc, points[^1]);
    }

    /// <summary>A ring on the road end the next click would snap to.</summary>
    private static void DrawRoadEndMark(DrawingContext dc, Point at)
    {
        dc.DrawEllipse(null, CursorShadowPen, at, 8, 8);
        dc.DrawEllipse(null, CursorPen, at, 8, 8);
        dc.DrawEllipse(Brushes.White, CursorShadowPen, at, 3, 3);
    }

    private static readonly uint TownPoiColour = OverlayRaster.Premultiply(Color.FromArgb(255, 255, 200, 90));
    private static readonly uint TownRemovedColour = OverlayRaster.Premultiply(Color.FromArgb(255, 255, 70, 60));
    private static readonly uint TownRemovedTint = OverlayRaster.Premultiply(Color.FromArgb(70, 255, 70, 60));
    private static readonly Pen TownSelectionPen = Frozen(new Pen(Brushes.White, 1.5) { DashStyle = DashStyles.Dash });
    private static readonly Pen TownStreetPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(220, 30, 30, 34)), 3));
    private static readonly Pen TownRoadPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(120, 255, 140)), 3)
    {
        StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round,
    });

    /// <summary>
    /// The planned town: tiles tinted by district with their streets, POIs (traders in magenta), prefabs it
    /// replaces in red, link roads in green, and the dragged selection.
    /// </summary>
    private void DrawTownPreview(DrawingContext dc)
    {
        if (_townPreview is not { } town) return;
        Rect Screen(Rect map) => new(MapToScreen(map.TopLeft), MapToScreen(map.BottomRight));

        _townOverlay.Begin(ActualWidth, ActualHeight, PixelsPerDip);
        foreach (var r in town.Removed)
        {
            _townOverlay.Fill(Screen(r), TownRemovedTint);
            _townOverlay.Outline(Screen(r), TownRemovedColour, HaloColour);
        }
        foreach (var t in town.Tiles)
        {
            var c = t.Colour;
            _townOverlay.Fill(Screen(t.MapRect), OverlayRaster.Premultiply(Color.FromArgb(105, c.R, c.G, c.B)));
            _townOverlay.Outline(Screen(t.MapRect), OverlayRaster.Premultiply(Color.FromArgb(255, c.R, c.G, c.B)), HaloColour);
        }
        foreach (var r in town.Pois) _townOverlay.Outline(Screen(r), TownPoiColour, HaloColour);
        foreach (var r in town.Traders)
        {
            _townOverlay.Fill(Screen(r), TraderTint);
            _townOverlay.Outline(Screen(r), TraderColour, HaloColour);
        }
        _townOverlay.End(dc, RenderSize);

        // Streets from each tile's middle to its exits, so the layout reads at a glance.
        foreach (var t in town.Tiles)
        {
            var s = Screen(t.MapRect);
            var centre = new Point(s.X + s.Width / 2, s.Y + s.Height / 2);
            if ((t.Exits & 1) != 0) dc.DrawLine(TownStreetPen, centre, new Point(centre.X, s.Top));
            if ((t.Exits & 2) != 0) dc.DrawLine(TownStreetPen, centre, new Point(s.Right, centre.Y));
            if ((t.Exits & 4) != 0) dc.DrawLine(TownStreetPen, centre, new Point(centre.X, s.Bottom));
            if ((t.Exits & 8) != 0) dc.DrawLine(TownStreetPen, centre, new Point(s.Left, centre.Y));
        }
        foreach (var road in town.Roads)
            for (int i = 1; i < road.Count; i++)
                dc.DrawLine(TownRoadPen, MapToScreen(road[i - 1]), MapToScreen(road[i]));
        foreach (var road in town.OffRoads)
            for (int i = 1; i < road.Count; i++)
                dc.DrawLine(TownRoadOffPen, MapToScreen(road[i - 1]), MapToScreen(road[i]));
        // While dragging: the cells the town will take, and the rectangle under the mouse (red if too small).
        foreach (var c in town.Cells) dc.DrawRectangle(TownCellFill, TownCellPen, Screen(c));
        if (town.Drag is { } drag)
        {
            var s = Screen(drag);
            dc.DrawRectangle(town.TooSmall ? TownTooSmallFill : null, town.TooSmall ? TownTooSmallPen : TownDragPen, s);
            if (town.TooSmall)
            {
                var text = new FormattedText("Too small: at least 150 × 150 m", System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, PixelsPerDip);
                var at = new Point(s.Left, s.Bottom + 4);
                dc.DrawRoundedRectangle(TownLabelBackground, null, new Rect(at.X - 4, at.Y - 2, text.Width + 8, text.Height + 4), 3, 3);
                dc.DrawText(text, at);
            }
        }
        if (town.Selection.Width > 0 && town.Selection.Height > 0)
        {
            var box = Screen(town.Selection);
            dc.DrawRectangle(null, CursorShadowPen, box);
            dc.DrawRectangle(null, TownSelectionPen, box);
            // Grab handles on the corners and the middle of each edge.
            if (town.Handles)
                foreach (var x in new[] { box.Left, box.Left + box.Width / 2, box.Right })
                    foreach (var y in new[] { box.Top, box.Top + box.Height / 2, box.Bottom })
                        if (x != box.Left + box.Width / 2 || y != box.Top + box.Height / 2)
                            dc.DrawRectangle(Brushes.White, CursorShadowPen, new Rect(x - 3.5, y - 3.5, 7, 7));
        }
    }

    private static readonly Pen TownRoadOffPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(220, 255, 110, 90)), 2)
    {
        DashStyle = DashStyles.Dash,
    });

    private static readonly Brush TownCellFill = Frozen(new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)));
    private static readonly Pen TownCellPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)), 1));
    private static readonly Pen TownDragPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), 1)
    {
        DashStyle = DashStyles.Dot,
    });
    private static readonly Brush TownTooSmallFill = Frozen(new SolidColorBrush(Color.FromArgb(50, 255, 60, 50)));
    private static readonly Pen TownTooSmallPen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(255, 80, 70)), 2)
    {
        DashStyle = DashStyles.Dash,
    });
    private static readonly Brush TownLabelBackground = Frozen(new SolidColorBrush(Color.FromArgb(200, 120, 20, 20)));

    private static readonly Brush RoadAsphaltBrush = Frozen(new SolidColorBrush(Color.FromArgb(150, 40, 40, 46)));
    private static readonly Brush RoadGravelBrush = Frozen(new SolidColorBrush(Color.FromArgb(150, 180, 154, 106)));

    private static Brush Frozen(Brush brush)
    {
        brush.Freeze();
        return brush;
    }

    private void DrawBrushCursor(DrawingContext dc)
    {
        if (_brushCursor is not { } brush || _mouseMap is not { } m) return;
        var centre = MapToScreen(m);
        double half = brush.SizeMetres / 2 * _scale;
        if (brush.Square)
        {
            var square = new Rect(centre.X - half, centre.Y - half, half * 2, half * 2);
            dc.PushTransform(new RotateTransform(brush.RotationDegrees, centre.X, centre.Y));
            if (brush.Picture is { } picture)
            {
                dc.PushOpacity(StampPictureOpacity);
                dc.DrawImage(picture, square);
                dc.Pop();
            }
            dc.DrawRectangle(null, CursorShadowPen, square);
            dc.DrawRectangle(null, CursorPen, square);
            dc.DrawLine(CursorPen, centre, new Point(centre.X, centre.Y - half)); // marks the stamp's "up"
            dc.Pop();
        }
        else
        {
            dc.DrawEllipse(null, CursorShadowPen, centre, half, half);
            dc.DrawEllipse(null, CursorPen, centre, half, half);
        }
    }

    private int Epoch(TileKey key) => _epochs.GetValueOrDefault((key.Level, key.X, key.Y));

    /// <summary>
    /// Marks every tile covering edited cells (grid cells, z from the south) as stale at every level,
    /// widened so hillshading at the edge picks up the new neighbours.
    /// </summary>
    public void InvalidateRegion(GridRect cells)
    {
        if (_renderer is null || cells.IsEmpty) return;
        int size = _renderer.World.Size;
        double u0 = cells.X0, u1 = cells.X1, v0 = size - cells.Z1, v1 = size - cells.Z0;
        for (int level = 0; level < _renderer.Levels; level++)
        {
            int step = MapRenderer.MetresPerPixel(level);
            int tileMetres = MapRenderer.TileSize << level;
            int across = _renderer.TilesAcross(level);
            int tx0 = Math.Max(0, (int)((u0 - 2 * step) / tileMetres)), tx1 = Math.Min(across - 1, (int)((u1 + 2 * step) / tileMetres));
            int ty0 = Math.Max(0, (int)((v0 - 2 * step) / tileMetres)), ty1 = Math.Min(across - 1, (int)((v1 + 2 * step) / tileMetres));
            for (int ty = ty0; ty <= ty1; ty++)
                for (int tx = tx0; tx <= tx1; tx++)
                    _epochs.AddOrUpdate((level, tx, ty), 1, (_, e) => e + 1);
        }
        InvalidateVisual();
    }

    private void DrawFallback(DrawingContext dc, TileKey key, Rect clip, int top, double dpi)
    {
        for (int a = key.Level + 1; a <= top; a++)
        {
            int shift = a - key.Level;
            var parent = new TileKey(a, key.X >> shift, key.Y >> shift, key.Generation);
            if (!_cache.TryGet(parent, out var bitmap, out _)) continue;
            dc.PushClip(new RectangleGeometry(clip));
            dc.DrawImage(bitmap, TileRect(parent.Level, parent.X, parent.Y, dpi));
            dc.Pop();
            return;
        }
    }

    /// <summary>Tile bounds in DIPs, snapped to device pixels so neighbouring tiles meet without seams.</summary>
    private Rect TileRect(int level, int tx, int ty, double dpi)
    {
        double metres = MapRenderer.TileSize << level;
        var a = MapToScreen(new Point(tx * metres, ty * metres));
        var b = MapToScreen(new Point((tx + 1) * metres, (ty + 1) * metres));
        double Snap(double v) => Math.Round(v * dpi) / dpi;
        return new Rect(new Point(Snap(a.X), Snap(a.Y)), new Point(Snap(b.X), Snap(b.Y)));
    }

    private void DrawPrefabs(DrawingContext dc, Rect visible)
    {
        var world = _renderer!.World;
        int half = world.Size / 2;
        _overlay.Begin(ActualWidth, ActualHeight, PixelsPerDip);
        Rect Screen(Rect map) => new(MapToScreen(map.TopLeft), MapToScreen(map.BottomRight));

        // Prefabs big enough on screen are drawn from above, street tiles first so POIs sit on them. Those keep
        // their outline but lose the tint, which would wash the picture out.
        var detailed = new HashSet<PrefabInstance>(ReferenceEqualityComparer.Instance);
        if (ShowPrefabDetail)
        {
            double devicePerMetre = _scale * PixelsPerDip;
            foreach (bool tiles in new[] { true, false })
                foreach (var p in world.Prefabs)
                {
                    if (p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal) != tiles) continue;
                    if (Prefabs.Find(p.Name) is not { } info) continue;
                    var (sx, sz) = info.Footprint(p.Rotation);
                    if (Math.Min(sx, sz) * devicePerMetre < DetailMinPixels) continue;
                    var rect = PrefabMapRect(p, half);
                    if (!rect.IntersectsWith(visible)) continue;
                    if (_detail.Get(info, p.Rotation) is not { } picture) continue;
                    dc.DrawImage(picture, Screen(rect));
                    detailed.Add(p);
                }
        }

        // Street tiles first, then their lots, then POIs and parts on top.
        foreach (var p in world.Prefabs)
        {
            if (!p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal)) continue;
            var rect = PrefabMapRect(p, half);
            if (!rect.IntersectsWith(visible)) continue;
            if (!detailed.Contains(p))
            {
                _overlay.Fill(Screen(rect), StreetTileTint);
                // Until its picture shows, a tile's streets are sketched from its shape and turn.
                if (rect.Width * _scale * PixelsPerDip >= StreetLineMinPixels) DrawStreetLines(p, Screen(rect));
            }
            _overlay.Outline(Screen(rect), StreetTileColour, HaloColour);
        }
        // Lots are only worth drawing once a 25 m lot is a few pixels across.
        if (_showLots && _scale >= 0.2)
        {
            foreach (var p in world.Prefabs)
            {
                if (Prefabs.Find(p.Name) is not { Markers.Count: > 0 } info) continue;
                if (!PrefabMapRect(p, half).IntersectsWith(visible)) continue;
                foreach (var m in info.RotatedMarkers(p.Rotation))
                {
                    if (m.Type != MarkerType.POISpawn) continue;
                    var lot = FootprintMapRect(p.Position.X + m.Start.X, p.Position.Z + m.Start.Z, m.Size.X, m.Size.Z);
                    _overlay.Outline(Screen(lot), LotColour);
                }
            }
        }
        var traders = new List<(Rect Rect, bool Detailed)>();
        foreach (var p in world.Prefabs)
        {
            if (p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal)) continue;
            var rect = PrefabMapRect(p, half);
            if (!rect.IntersectsWith(visible)) continue;
            if (Prefabs.Find(p.Name)?.IsPart == true)
            {
                // Parts are small props; outline them only once they are big enough to tell apart.
                if (_scale >= 0.5) _overlay.Outline(Screen(rect), PartColour);
                continue;
            }
            if (Prefabs.Find(p.Name)?.IsTrader == true)
            {
                traders.Add((rect, detailed.Contains(p)));
                continue;
            }
            if (!detailed.Contains(p)) _overlay.Fill(Screen(rect), PrefabTint);
            _overlay.Outline(Screen(rect), PrefabColour, HaloColour);
        }
        // Traders last, in their own colour with a double line, so they stand out from the town around them.
        foreach (var (rect, isDetailed) in traders)
        {
            var s = Screen(rect);
            if (!isDetailed) _overlay.Fill(s, TraderTint);
            _overlay.Outline(s, TraderColour, HaloColour);
            if (s.Width > 8 && s.Height > 8) _overlay.Outline(Rect.Inflate(s, -1 / PixelsPerDip, -1 / PixelsPerDip), TraderColour);
            else _overlay.Outline(Rect.Inflate(s, 3, 3), TraderColour, HaloColour); // keep them findable when zoomed out
        }
        _overlay.End(dc, RenderSize);

        if (_hoverPrefab is { } hover && !ReferenceEquals(hover, _selectedPrefab))
        {
            var rect = PrefabMapRect(hover, half);
            dc.DrawRectangle(HoverFill, HoverPen, new Rect(MapToScreen(rect.TopLeft), MapToScreen(rect.BottomRight)));
        }
        if (_selectedPrefab is { } selected)
        {
            var rect = PrefabMapRect(selected, half);
            var screen = new Rect(MapToScreen(rect.TopLeft), MapToScreen(rect.BottomRight));
            dc.DrawRectangle(SelectedFill, SelectedPen, screen);
            if (Prefabs.Find(selected.Name) is { } info) DrawFrontArrow(dc, screen, info.FrontDirection(selected.Rotation), SelectedPen);
        }
    }

    private void DrawPrefabGhost(DrawingContext dc)
    {
        if (_prefabGhost is not { } ghost) return;
        foreach (var r in ghost.Replaces)
            dc.DrawRectangle(GhostReplaceFill, GhostReplacePen, new Rect(MapToScreen(r.TopLeft), MapToScreen(r.BottomRight)));
        if (ghost.Lot is { } lot)
            dc.DrawRectangle(null, ghost.LotFits ? LotPen : GhostWarnPen, new Rect(MapToScreen(lot.TopLeft), MapToScreen(lot.BottomRight)));
        var screen = new Rect(MapToScreen(ghost.MapRect.TopLeft), MapToScreen(ghost.MapRect.BottomRight));
        var pen = ghost.Warning ? GhostWarnPen : GhostPen;
        // The prefab seen from above, see-through so the map under it still shows, for lining it up with roads.
        // Until its picture is built (Built redraws) the plain tint stands in.
        if (ghost.Info is { } info && _detail.Get(info, ghost.Rotation) is { } picture)
        {
            dc.PushOpacity(GhostPictureOpacity);
            dc.DrawImage(picture, screen);
            dc.Pop();
            dc.DrawRectangle(ghost.Warning ? GhostWarnFill : null, pen, screen);
        }
        else dc.DrawRectangle(ghost.Warning ? GhostWarnFill : GhostFill, pen, screen);
        if (ghost.Front >= 0) DrawFrontArrow(dc, screen, ghost.Front, pen);
    }

    /// <summary>An arrow from the centre of <paramref name="screen"/> toward the prefab's front (0 N, 1 W, 2 S, 3 E).</summary>
    private static void DrawFrontArrow(DrawingContext dc, Rect screen, int front, Pen pen)
    {
        var c = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
        var dir = front switch { 0 => new Vector(0, -1), 1 => new Vector(-1, 0), 2 => new Vector(0, 1), _ => new Vector(1, 0) };
        double reach = (front % 2 == 0 ? screen.Height : screen.Width) / 2;
        if (reach < 6) return;
        double head = Math.Min(10, reach * 0.4);
        var tip = c + dir * reach * 0.9;
        var side = new Vector(-dir.Y, dir.X);
        dc.DrawLine(pen, c, tip);
        dc.DrawLine(pen, tip, tip - dir * head + side * head * 0.6);
        dc.DrawLine(pen, tip, tip - dir * head - side * head * 0.6);
    }

    /// <summary>A footprint whose south-west corner is at world (x, z), in map coordinates.</summary>
    public Rect FootprintMapRect(int x, int z, int sizeX, int sizeZ)
    {
        int size = _renderer?.World.Size ?? 0;
        int half = size / 2;
        return new Rect(x + half, size - (z + half + sizeZ), sizeX, sizeZ);
    }

    private const double GhostPictureOpacity = 0.75;

    /// <summary>The stamp picture is fainter than a prefab's, so the ground it lands on still shows.</summary>
    private const double StampPictureOpacity = 0.6;

    private static readonly Brush GhostReplaceFill = Frozen(new SolidColorBrush(Color.FromArgb(90, 255, 70, 60)));
    private static readonly Pen GhostReplacePen = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(255, 80, 70)), 2));

    /// <summary>On-screen size (device pixels, shorter side) at which a prefab is drawn from above.</summary>
    private const double DetailMinPixels = 40;

    /// <summary>On-screen size (device pixels) at which a street tile without its picture gets sketched streets.</summary>
    private const double StreetLineMinPixels = 12;

    private static readonly uint StreetLineColour = OverlayRaster.Premultiply(Color.FromArgb(235, 60, 60, 66));

    /// <summary>
    /// Sketches a street tile's roads: a 10 m line from the middle to each exit, from the tile's shape (the end of
    /// its name) and its turn, as the town generator lays them out.
    /// </summary>
    private void DrawStreetLines(PrefabInstance p, Rect screen)
    {
        if (Prefabs.Find(p.Name) is not { } info) return;
        // The shape is the last word of the name, before any variant number (rwg_tile_industrial_t_03).
        var words = p.Name.Split('_');
        int last = words.Length - 1;
        if (last > 0 && words[last].All(char.IsDigit)) last--;
        int shape = Array.IndexOf(TownGenerator.ShapeNames, words[last]);
        if (shape < 0) return;
        // TileRotation swaps 1 and 3; undo that, then take off the turn that faces the tile north.
        int turn = (p.Rotation & 3) switch { 1 => 3, 3 => 1, var r => r };
        int exits = TownGenerator.ShapeExitsAt(shape, (turn - info.RotationToFaceNorth) & 3);
        double half = Math.Max(5 * _scale, 1 / PixelsPerDip);
        var c = new Point(screen.X + screen.Width / 2, screen.Y + screen.Height / 2);
        if ((exits & 1) != 0) _overlay.Bar(new Rect(c.X - half, screen.Top, half * 2, c.Y - screen.Top + half), StreetLineColour);
        if ((exits & 2) != 0) _overlay.Bar(new Rect(c.X - half, c.Y - half, screen.Right - c.X + half, half * 2), StreetLineColour);
        if ((exits & 4) != 0) _overlay.Bar(new Rect(c.X - half, c.Y - half, half * 2, screen.Bottom - c.Y + half), StreetLineColour);
        if ((exits & 8) != 0) _overlay.Bar(new Rect(screen.Left, c.Y - half, c.X - screen.Left + half, half * 2), StreetLineColour);
    }

    /// <summary>A prefab's footprint in map coordinates (its position is the south-west min corner in world coords).</summary>
    private Rect PrefabMapRect(PrefabInstance p, int half)
    {
        var (sx, sz) = Prefabs.Find(p.Name)?.Footprint(p.Rotation) ?? (8, 8);
        int size = half * 2;
        double u = p.Position.X + half;
        double vTop = size - (p.Position.Z + half + sz);
        return new Rect(u, vTop, sx, sz);
    }

    /// <summary>A new spawn point as it would be added: facing north, like <see cref="DrawSpawns"/> draws them.</summary>
    private void DrawSpawnGhost(DrawingContext dc)
    {
        if (_spawnGhost is not { } m || _renderer is null) return;
        var c = MapToScreen(m);
        dc.PushOpacity(0.6);
        dc.DrawLine(CursorShadowPen, c, c + new Vector(0, -16));
        dc.DrawLine(SpawnFacingPen, c, c + new Vector(0, -16));
        dc.DrawEllipse(SpawnFill, SpawnPen, c, 5, 5);
        dc.Pop();
    }

    private void DrawSpawns(DrawingContext dc, Rect visible)
    {
        var world = _renderer!.World;
        int half = world.Size / 2;
        foreach (var s in world.Spawns)
        {
            var m = new Point(s.X + half, world.Size - (s.Z + half));
            if (!visible.Contains(m)) continue;
            var c = MapToScreen(m);
            // The tick shows which way the player faces: RotationY is degrees clockwise from north.
            double a = s.RotationY * Math.PI / 180;
            var facing = new Vector(Math.Sin(a), -Math.Cos(a));
            bool selected = ReferenceEquals(s, _selectedSpawn);
            dc.DrawLine(CursorShadowPen, c, c + facing * 16);
            dc.DrawLine(SpawnFacingPen, c, c + facing * 16);
            dc.DrawEllipse(selected ? SelectedSpawnFill : SpawnFill, SpawnPen, c, selected ? 7 : 5, selected ? 7 : 5);
        }
    }

    // ---- tile generation ---------------------------------------------------

    private void ResetTiles()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
        _queue.Clear();
        _pending.Clear();
        _epochs.Clear();
        InvalidateVisual();
    }

    private void Request(TileKey key)
    {
        var item = (key, Epoch(key));
        if (!_pending.TryAdd(item, 0)) return;
        _queue.Push(item);
        StartWorkerIfRoom();
    }

    private void WorkerLoop()
    {
        var buffer = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        try
        {
            while (_queue.TryPop(out var item))
            {
                var (key, epoch) = item;
                try
                {
                    var renderer = _renderer;
                    // Skip work no longer needed: old layers, off screen, or superseded by a newer edit.
                    if (renderer is null || key.Generation != _generation || !_wanted.Contains(key) || epoch != Epoch(key)) continue;
                    renderer.RenderTile(key.Level, key.X, key.Y, (MapLayers)_layers, buffer);
                    if (key.Generation != _generation) continue;
                    var bitmap = BitmapSource.Create(MapRenderer.TileSize, MapRenderer.TileSize, 96, 96,
                        PixelFormats.Pbgra32, null, buffer, MapRenderer.TileSize * 4);
                    bitmap.Freeze();
                    _cache.Add(key, bitmap, epoch);
                    QueueInvalidate();
                }
                finally
                {
                    _pending.TryRemove(item, out _);
                }
            }
        }
        finally
        {
            Interlocked.Decrement(ref _workers);
        }

        // A tile pushed just as this worker gave up would otherwise wait for the next request.
        if (!_queue.IsEmpty) StartWorkerIfRoom();
    }

    private void StartWorkerIfRoom()
    {
        if (Interlocked.Increment(ref _workers) <= Math.Max(1, Environment.ProcessorCount - 1))
            Task.Run(WorkerLoop);
        else
            Interlocked.Decrement(ref _workers);
    }

    private void QueueInvalidate()
    {
        if (Interlocked.Exchange(ref _invalidateQueued, 1) == 1) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, () =>
        {
            _invalidateQueued = 0;
            InvalidateVisual();
        });
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
