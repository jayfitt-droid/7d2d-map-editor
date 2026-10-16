using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
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
    private Point? _dragStart;
    private Point _dragOrigin;
    private bool _toolDrag;
    private Point? _mouseMap;

    // Prefab outlines are drawn in software (see OverlayRaster); colours are premultiplied BGRA.
    private static readonly uint PrefabColour = OverlayRaster.Premultiply(Color.FromArgb(220, 255, 160, 40));
    private static readonly uint StreetTileColour = OverlayRaster.Premultiply(Color.FromArgb(70, 255, 255, 255));
    private readonly OverlayRaster _overlay = new();
    private static readonly Pen HoverPen = Frozen(new Pen(Brushes.White, 2));
    private static readonly Brush HoverFill = Frozen(new SolidColorBrush(Color.FromArgb(50, 255, 255, 255)));
    private static readonly Brush SpawnFill = Frozen(new SolidColorBrush(Color.FromRgb(60, 220, 90)));
    private static readonly Pen SpawnPen = Frozen(new Pen(Brushes.Black, 1));
    private static readonly Brush Background = Frozen(new SolidColorBrush(Color.FromRgb(24, 25, 28)));
    private static readonly Pen CursorPen = Frozen(new Pen(Brushes.White, 1.5));
    private static readonly Pen CursorShadowPen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), 3.5));

    public MapView()
    {
        Focusable = true;
        ClipToBounds = true;
        RenderOptions.SetEdgeMode(this, EdgeMode.Aliased);
        SizeChanged += (_, _) =>
        {
            if (_needsFit) FitToView();
            OnViewChanged();
        };
    }

    public event EventHandler? ViewChanged;

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

    public PrefabLibrary Prefabs { get; set; } = PrefabLibrary.Empty;

    /// <summary>When true, left-button drags go to <see cref="ToolInput"/>; the middle button still pans.</summary>
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
    public RoadPreview? RoadPreview
    {
        get => _roadPreview;
        set
        {
            _roadPreview = value;
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

    public bool ShowSpawns
    {
        get => (bool)GetValue(ShowSpawnsProperty);
        set => SetValue(ShowSpawnsProperty, value);
    }

    public static readonly DependencyProperty ShowSpawnsProperty = DependencyProperty.Register(
        nameof(ShowSpawns), typeof(bool), typeof(MapView),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

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
    }

    public void CenterOn(Point map)
    {
        _origin = new Point(map.X - ActualWidth / 2 / _scale, map.Y - ActualHeight / 2 / _scale);
        OnViewChanged();
    }

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
        if (e.ChangedButton == MouseButton.Left && ToolActive && _renderer is not null)
        {
            _toolDrag = true;
            CaptureMouse();
            ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Down, ScreenToMap(e.GetPosition(this)), Keyboard.Modifiers, e.ClickCount));
            e.Handled = true;
            return;
        }
        if (e.ChangedButton is MouseButton.Left or MouseButton.Middle)
        {
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
        if (_dragStart is null) return;
        _dragStart = null;
        ReleaseMouseCapture();
        Cursor = null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_dragStart is { } start)
        {
            _origin = new Point(_dragOrigin.X - (p.X - start.X) / _scale, _dragOrigin.Y - (p.Y - start.Y) / _scale);
            OnViewChanged();
        }
        var map = ScreenToMap(p);
        if (_toolDrag) ToolInput?.Invoke(this, new ToolInputEventArgs(ToolPhase.Move, map, Keyboard.Modifiers));
        _mouseMap = map;
        if (_brushCursor is not null || _roadPreview is not null) InvalidateVisual();
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
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (_renderer is null) return;

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

        if (ShowPrefabs) DrawPrefabs(dc, visible);
        if (ShowSpawns) DrawSpawns(dc, visible);
        DrawRoadPreview(dc);
        DrawBrushCursor(dc);
    }

    private void DrawRoadPreview(DrawingContext dc)
    {
        if (_roadPreview is not { MapPoints.Count: > 0 } road) return;
        var points = road.MapPoints.Select(MapToScreen).ToList();
        if (_mouseMap is { } m) points.Add(MapToScreen(m));

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
    }

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
        // Street tiles first so POI outlines sit on top of them.
        foreach (bool streetTiles in (ReadOnlySpan<bool>)[true, false])
        {
            uint colour = streetTiles ? StreetTileColour : PrefabColour;
            foreach (var p in world.Prefabs)
            {
                if (p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal) != streetTiles) continue;
                var rect = PrefabMapRect(p, half);
                if (!rect.IntersectsWith(visible)) continue;
                _overlay.Outline(new Rect(MapToScreen(rect.TopLeft), MapToScreen(rect.BottomRight)), colour);
            }
        }
        _overlay.End(dc, RenderSize);

        if (_hoverPrefab is { } hover)
        {
            var rect = PrefabMapRect(hover, half);
            dc.DrawRectangle(HoverFill, HoverPen, new Rect(MapToScreen(rect.TopLeft), MapToScreen(rect.BottomRight)));
        }
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

    private void DrawSpawns(DrawingContext dc, Rect visible)
    {
        var world = _renderer!.World;
        int half = world.Size / 2;
        foreach (var s in world.Spawns)
        {
            var m = new Point(s.X + half, world.Size - (s.Z + half));
            if (!visible.Contains(m)) continue;
            dc.DrawEllipse(SpawnFill, SpawnPen, MapToScreen(m), 5, 5);
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
