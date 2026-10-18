using System.ComponentModel;
using System.Numerics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>
/// The Town tool: drag a rectangle, and a town is planned in the 150 m cells inside it the way RWG would plan
/// one. The rectangle snaps to whole cells counted from the corner the drag starts at, so the town's edge is
/// where it was drawn. The plan is only a preview until it is built (Enter), as one undo step; changing the
/// township, seed or options plans again. Before it is built, the box's edges and corners can be dragged to resize it
/// (in whole cells), and clicking a link road turns it off or back on.
/// </summary>
public sealed class TownToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly UndoStack _undo;

    private const int Cell = PrefabPlacer.StreetTileSize;

    private Point? _dragStart; // grid metres (x east, z north)
    private GridRect? _selection; // snapped to whole cells
    private GridRect? _drag; // the rectangle under the mouse, while dragging or after a drag too small for a cell
    private TownPlan? _plan;
    private bool _choosingDefault;

    [Flags]
    private enum Edges { None = 0, West = 1, East = 2, South = 4, North = 8 }

    private Edges _resize; // the edges being dragged, while resizing the box
    private GridRect _resizeFrom;

    // Link roads the user turned off, by the exit they start from, so a replan (new seed, options) keeps them off.
    private readonly HashSet<(int X, int Z)> _offExits = [];
    private readonly List<RoadPath> _offRoads = [];

    /// <summary>How near (in screen pixels) the mouse must be to the box's edge, or to a link road, to grab it.</summary>
    private const double GrabPixels = 8;

    public TownToolController(MapView map, MainViewModel vm, UndoStack undo)
    {
        _map = map;
        _vm = vm;
        _undo = undo;
        _map.ToolInput += OnToolInput;
        _map.CursorMoved += OnCursorMoved;
        _vm.PropertyChanged += OnOptionChanged;
    }

    public World? World { get; set; }

    /// <summary>Raised after a town has been built and committed to the undo history.</summary>
    public event EventHandler<GridRect>? Edited;

    public bool IsDragging => _dragStart is not null || _resize != Edges.None;

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_choosingDefault) return;
        if (e.PropertyName == nameof(MainViewModel.SelectedTownship) && !_vm.LoadingTownships && _vm.SelectedTownship is not null)
            _vm.TownshipPicked = true;
        if (_selection is null) return;
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.SelectedTownship):
            case nameof(MainViewModel.TownSeed):
            case nameof(MainViewModel.TownReplace):
            case nameof(MainViewModel.TownRoads):
            case nameof(MainViewModel.TownLinkStyle):
            case nameof(MainViewModel.TownParts):
            case nameof(MainViewModel.TownTraders):
                Replan();
                break;
        }
    }

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world || _vm.SelectedTool != EditorTool.Town) return;
        if (_vm.IsBusy && e.Phase == ToolPhase.Down) return;
        var grid = new Point(Math.Clamp(e.MapPoint.X, 0, world.Size), Math.Clamp(world.Size - e.MapPoint.Y, 0, world.Size));
        switch (e.Phase)
        {
            case ToolPhase.Down when _selection is { } current && EdgesAt(current, grid) is var edges and not Edges.None:
                _resize = edges;
                _resizeFrom = current;
                _plan = null;
                _offRoads.Clear();
                ShowPreview();
                break;
            case ToolPhase.Down when _plan is not null && RoadAt(grid) is { } road:
                ToggleRoad(road);
                break;
            case ToolPhase.Move when _resize != Edges.None:
                _selection = Resized(_resizeFrom, _resize, grid, world.Size);
                ShowPreview();
                break;
            case ToolPhase.Up when _resize != Edges.None:
                _selection = Resized(_resizeFrom, _resize, grid, world.Size);
                _resize = Edges.None;
                ChooseDefaultTownship(world, _selection.Value, TownGenerator.CellsIn(world, _selection.Value).Count);
                Replan();
                break;
            case ToolPhase.Down:
                _offExits.Clear();
                _dragStart = grid;
                _plan = null;
                _drag = Rect(grid, grid);
                _selection = Snapped(grid, grid, world.Size);
                ShowPreview();
                break;
            case ToolPhase.Move when _dragStart is { } start:
                _drag = Rect(start, grid);
                _selection = Snapped(start, grid, world.Size);
                ShowPreview();
                break;
            case ToolPhase.Up when _dragStart is { } start:
                _dragStart = null;
                _drag = Rect(start, grid);
                _selection = Snapped(start, grid, world.Size);
                var cells = TownGenerator.CellsIn(world, _selection.Value);
                if (cells.Count == 0)
                {
                    // Keep the red rectangle up so it is clear what was too small.
                    _selection = null;
                    ShowPreview();
                    _vm.Status = "Too small for a town: drag at least 150 m each way (one street tile).";
                    return;
                }
                _drag = null;
                ChooseDefaultTownship(world, _selection.Value, cells.Count);
                Replan();
                break;
        }
    }

    private static GridRect Rect(Point a, Point b) =>
        new((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y)));

    /// <summary>
    /// The drag from <paramref name="start"/> to <paramref name="end"/> cut down to whole 150 m cells counted from
    /// the start corner, and kept inside the world. Empty when it does not reach a whole cell either way.
    /// </summary>
    private static GridRect Snapped(Point start, Point end, int worldSize)
    {
        int sx = (int)Math.Round(start.X), sz = (int)Math.Round(start.Y);
        static int Count(double distance, int room) => Math.Min((int)(Math.Abs(distance) / Cell), room / Cell);
        double dx = end.X - sx, dz = end.Y - sz;
        int nx = Count(dx, dx >= 0 ? worldSize - sx : sx), nz = Count(dz, dz >= 0 ? worldSize - sz : sz);
        int x0 = dx >= 0 ? sx : sx - nx * Cell, z0 = dz >= 0 ? sz : sz - nz * Cell;
        return new GridRect(x0, z0, x0 + nx * Cell, z0 + nz * Cell);
    }

    /// <summary>Which edges of the box the point is on (two at a corner), within <see cref="GrabPixels"/>.</summary>
    private Edges EdgesAt(GridRect box, Point grid)
    {
        double t = GrabPixels / _map.Scale;
        bool alongX = grid.X >= box.X0 - t && grid.X <= box.X1 + t, alongZ = grid.Y >= box.Z0 - t && grid.Y <= box.Z1 + t;
        var edges = Edges.None;
        if (alongZ && Math.Abs(grid.X - box.X0) <= t) edges |= Edges.West;
        else if (alongZ && Math.Abs(grid.X - box.X1) <= t) edges |= Edges.East;
        if (alongX && Math.Abs(grid.Y - box.Z0) <= t) edges |= Edges.South;
        else if (alongX && Math.Abs(grid.Y - box.Z1) <= t) edges |= Edges.North;
        return edges;
    }

    /// <summary>
    /// The box with the dragged edges moved to the whole cell nearest the mouse, at least one cell from the edge
    /// opposite and inside the world. The other edges stay where they are.
    /// </summary>
    private static GridRect Resized(GridRect from, Edges edges, Point grid, int worldSize)
    {
        int x0 = from.X0, x1 = from.X1, z0 = from.Z0, z1 = from.Z1;
        static int Cells(double distance, int room) => Math.Clamp((int)Math.Round(distance / Cell), 1, Math.Max(1, room / Cell));
        if (edges.HasFlag(Edges.West)) x0 = x1 - Cells(x1 - grid.X, x1) * Cell;
        if (edges.HasFlag(Edges.East)) x1 = x0 + Cells(grid.X - x0, worldSize - x0) * Cell;
        if (edges.HasFlag(Edges.South)) z0 = z1 - Cells(z1 - grid.Y, z1) * Cell;
        if (edges.HasFlag(Edges.North)) z1 = z0 + Cells(grid.Y - z0, worldSize - z0) * Cell;
        return new GridRect(x0, z0, x1, z1);
    }

    private static (int X, int Z) ExitOf(RoadPath road) => ((int)MathF.Round(road.Points[0].X), (int)MathF.Round(road.Points[0].Y));

    /// <summary>The planned link road (on or off) under the point, if any.</summary>
    private RoadPath? RoadAt(Point grid)
    {
        if (_plan is not { } plan) return null;
        var p = new Vector2((float)grid.X, (float)grid.Y);
        float grab = (float)(GrabPixels / _map.Scale);
        return plan.Roads.Concat(_offRoads)
            .Select(r => (Road: r, Distance: DistanceTo(r.Points, p) - r.Radius))
            .Where(r => r.Distance <= grab)
            .OrderBy(r => r.Distance)
            .Select(r => r.Road)
            .FirstOrDefault();
    }

    private static float DistanceTo(IReadOnlyList<Vector2> line, Vector2 p)
    {
        float best = float.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            var ab = line[i] - line[i - 1];
            float t = Math.Clamp(Vector2.Dot(p - line[i - 1], ab) / Math.Max(ab.LengthSquared(), 1e-6f), 0, 1);
            best = Math.Min(best, Vector2.Distance(p, line[i - 1] + ab * t));
        }
        return best;
    }

    /// <summary>Turns a link road off (its exit is left unjoined) or back on.</summary>
    private void ToggleRoad(RoadPath road)
    {
        if (_plan is not { } plan) return;
        if (plan.Roads.Remove(road))
        {
            _offRoads.Add(road);
            _offExits.Add(ExitOf(road));
        }
        else if (_offRoads.Remove(road))
        {
            plan.Roads.Add(road);
            _offExits.Remove(ExitOf(road));
        }
        ShowPreview();
    }

    /// <summary>Resize arrows over the box's edges and a hand over link roads, so it's clear they can be grabbed.</summary>
    private void OnCursorMoved(object? sender, Point? map)
    {
        if (World is not { } world || _vm.SelectedTool != EditorTool.Town || IsDragging || _map.IsPanning) return;
        Cursor? cursor = null;
        if (map is { } m && _selection is { } box)
        {
            var grid = new Point(m.X, world.Size - m.Y);
            cursor = EdgesAt(box, grid) switch
            {
                Edges.West or Edges.East => Cursors.SizeWE,
                Edges.North or Edges.South => Cursors.SizeNS,
                Edges.West | Edges.North or Edges.East | Edges.South => Cursors.SizeNWSE,
                Edges.West | Edges.South or Edges.East | Edges.North => Cursors.SizeNESW,
                _ => RoadAt(grid) is not null ? Cursors.Hand : null,
            };
        }
        if (_map.Cursor != cursor) _map.Cursor = cursor;
    }

    /// <summary>
    /// Offers the township RWG would put here: allowed in the biome at the centre, sized for the selection. Once the
    /// user has picked a township themselves, their pick is kept.
    /// </summary>
    private void ChooseDefaultTownship(World world, GridRect selection, int cells)
    {
        if (_vm.TownshipPicked) return;
        string? biome = TownGenerator.BiomeTagAt(world, _vm.Biomes, (selection.X0 + selection.X1) / 2, (selection.Z0 + selection.Z1) / 2);
        if (TownGenerator.DefaultTownship(_vm.Mixer, biome, cells) is not { } choice) return;
        _choosingDefault = true;
        try
        {
            _vm.SelectedTownship = _vm.Townships.FirstOrDefault(t => t.Name == choice.Name) ?? _vm.SelectedTownship;
        }
        finally
        {
            _choosingDefault = false;
        }
    }

    /// <summary>Reroll: plans again with a new seed.</summary>
    public void Reroll() => _vm.TownSeed = Random.Shared.Next(1, 1_000_000);

    private void Replan()
    {
        _plan = null;
        _offRoads.Clear();
        if (World is not { } world || _selection is not { } selection) { ShowPreview(); return; }
        if (_vm.SelectedTownship is not { } township)
        {
            ShowPreview();
            _vm.TownText = _vm.Prefabs.Count == 0
                ? "No game data: set the game folder under Settings to plan towns."
                : "rwgmixer.xml lists no townships to build.";
            return;
        }
        var options = new TownOptions(township, _vm.TownSeed)
        {
            ReplacePrefabs = _vm.TownReplace,
            ConnectRoads = _vm.TownRoads,
            LinkStyle = _vm.TownLinkStyle,
            AddParts = _vm.TownParts,
            Traders = _vm.TownTraders,
        };
        _plan = TownGenerator.Plan(world, _vm.Prefabs, _vm.Mixer, _vm.Biomes, selection, options);
        _offRoads.Clear();
        foreach (var road in _plan.Roads.Where(r => _offExits.Contains(ExitOf(r))).ToList())
        {
            _plan.Roads.Remove(road);
            _offRoads.Add(road);
        }
        ShowPreview();
    }

    /// <summary>Enter: builds the planned town as one undo step.</summary>
    public bool Accept()
    {
        if (World is not { } world || _plan is not { Tiles.Count: > 0 } plan) return false;
        var session = new EditSession($"Build {plan.Township.Name}");
        var changed = TownGenerator.Apply(world, _vm.Prefabs, plan, session);
        _map.InvalidateRegion(changed);
        _map.InvalidateRegion(session.Bounds);
        if (session.HasChanges)
        {
            _undo.Push(session);
            Edited?.Invoke(this, session.Bounds);
        }
        _vm.Status = $"Built a {plan.Township.Name}: {plan.Tiles.Count} tiles, {plan.PoiCount} POIs"
                     + (plan.TraderCount > 0 ? $" ({plan.TraderCount} trader{(plan.TraderCount == 1 ? "" : "s")})" : "")
                     + (plan.Roads.Count > 0 ? $", {plan.Roads.Count} link road{(plan.Roads.Count == 1 ? "" : "s")}" : "")
                     + (plan.Removed.Count > 0 ? $"; replaced {plan.Removed.Count} prefabs." : ".");
        Clear();
        return true;
    }

    /// <summary>Esc: drops the plan and selection. Returns false if there was nothing to cancel.</summary>
    public bool Cancel()
    {
        if (_selection is null && _plan is null && _drag is null) return false;
        Clear();
        return true;
    }

    /// <summary>After undo/redo, a new world or a tool change: plans again on the current world, or clears.</summary>
    public void Refresh()
    {
        _dragStart = null;
        _resize = Edges.None;
        if (World is null || _vm.SelectedTool != EditorTool.Town)
        {
            Clear();
            if (!_map.IsPanning) _map.Cursor = null;
        }
        else if (_selection is not null) Replan();
        else ShowPreview();
    }

    private void Clear()
    {
        _dragStart = null;
        _resize = Edges.None;
        _drag = null;
        _selection = null;
        _plan = null;
        _offExits.Clear();
        _offRoads.Clear();
        ShowPreview();
    }

    // ---- display -------------------------------------------------------------

    private void ShowPreview()
    {
        _vm.HasTownPlan = _plan is { Tiles.Count: > 0 };
        if (World is not { } world || (_selection is null && _drag is null))
        {
            _map.TownPreview = null;
            _vm.TownText = _vm.SelectedTool == EditorTool.Town
                ? "Drag a rectangle from one corner of the town; it snaps to whole 150 m street tiles."
                : "";
            return;
        }
        Rect MapRect(GridRect r) => new(r.X0, world.Size - r.Z1, r.X1 - r.X0, r.Z1 - r.Z0);
        var selection = _selection ?? GridRect.Empty;

        if (_plan is not { } plan)
        {
            var cells = TownGenerator.CellsIn(world, selection);
            int across = (selection.X1 - selection.X0) / Cell, up = (selection.Z1 - selection.Z0) / Cell;
            bool tooSmall = cells.Count == 0;
            _map.TownPreview = new TownPreview(MapRect(selection), [], [], [], [], [])
            {
                Drag = _drag is { } d ? MapRect(d) : null,
                Cells = cells.Select(c => MapRect(new GridRect(selection.X0 + c.Cx * Cell, selection.Z0 + c.Cz * Cell,
                    selection.X0 + c.Cx * Cell + Cell, selection.Z0 + c.Cz * Cell + Cell))).ToList(),
                TooSmall = tooSmall,
            };
            var drag = _drag ?? selection;
            _vm.TownText = tooSmall
                ? $"Too small for a town: {drag.X1 - drag.X0} × {drag.Z1 - drag.Z0} m. Drag at least 150 m each way (one street tile)."
                : $"{across} × {up} tiles ({across * Cell} × {up * Cell} m), {cells.Count} in all";
            return;
        }

        var tiles = plan.Tiles.Select(t =>
        {
            var (r, g, b) = t.District!.PreviewColour;
            return new TownTilePreview(MapRect(t.Cells), Color.FromRgb(r, g, b), t.Exits);
        }).ToList();
        var pois = new List<Rect>();
        var traders = new List<Rect>();
        foreach (var p in plan.Prefabs)
        {
            if (_vm.Prefabs.Find(p.Name) is not { IsStreetTile: false, IsPart: false } info) continue;
            var (sx, sz) = info.Footprint(p.Rotation);
            (info.IsTrader ? traders : pois).Add(_map.FootprintMapRect(p.Position.X, p.Position.Z, sx, sz));
        }
        var removed = plan.Removed
            .Where(p => _vm.Prefabs.Find(p.Name) is not { IsPart: true })
            .Select(p => MapRect(PrefabPlacer.Footprint(world, p, _vm.Prefabs))).ToList();
        var roads = plan.Roads.Select(r => (IReadOnlyList<Point>)r.Points.Select(v => new Point(v.X, world.Size - v.Y)).ToList()).ToList();
        var off = _offRoads.Select(r => (IReadOnlyList<Point>)r.Points.Select(v => new Point(v.X, world.Size - v.Y)).ToList()).ToList();
        _map.TownPreview = new TownPreview(MapRect(selection), tiles, pois, traders, removed, roads) { OffRoads = off, Handles = true };
        _vm.TownText = Describe(plan, _offRoads.Count);
    }

    private static string Describe(TownPlan plan, int roadsOff)
    {
        if (plan.Tiles.Count == 0) return "Can't plan a town here: " + string.Join("; ", plan.Warnings) + ".";
        var districts = plan.Tiles.GroupBy(t => t.District!.Name).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} {g.Count()}");
        var (min, max) = plan.Township.Tiles;
        string size = plan.Tiles.Count < min || plan.Tiles.Count > max
            ? $"\nRWG makes a {plan.Township.Name} {min}–{max} tiles on a world this size."
            : "";
        return $"{plan.Tiles.Count} tiles: {string.Join(", ", districts)}\n"
               + $"{plan.PoiCount} POIs ({plan.TraderCount} trader{(plan.TraderCount == 1 ? "" : "s")}), {plan.PartCount} parts\n"
               + $"Ground {plan.Ground} m"
               + (plan.Removed.Count > 0 ? $", replacing {plan.Removed.Count} prefabs (red)" : "")
               + (plan.Roads.Count > 0 ? $"\n{plan.Roads.Count} link road{(plan.Roads.Count == 1 ? "" : "s")} to the nearest road" : "")
               + (roadsOff > 0 ? $"{(plan.Roads.Count > 0 ? ", " : "\n")}{roadsOff} turned off" : "")
               + (plan.Roads.Count + roadsOff > 0 ? " (click a link road to turn it off or on)" : "")
               + "\nDrag the box's edges or corners to resize it."
               + size
               + (plan.Warnings.Count > 0 ? "\nNote: " + string.Join("; ", plan.Warnings) : "");
    }
}
