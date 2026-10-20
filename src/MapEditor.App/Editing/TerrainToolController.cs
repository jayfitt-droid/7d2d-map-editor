using System.Numerics;
using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>Turns tool input on the map into terrain and layer edits, one undo step per stroke.</summary>
public sealed class TerrainToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly UndoStack _undo;
    private EditSession? _session;
    private Point _last; // grid coords (x east, z north) of the previous dab
    private Noise? _noise;
    private bool _erase; // Shift held at stroke start: erase radiation or roads, drain water
    private readonly List<Vector2> _roadPoints = []; // grid metres, while a road is being drawn

    public TerrainToolController(MapView map, MainViewModel vm, UndoStack undo)
    {
        _map = map;
        _vm = vm;
        _undo = undo;
        _map.ToolInput += OnToolInput;
    }

    public World? World { get; set; }

    /// <summary>Raised after a stroke or stamp has been committed to the undo history.</summary>
    public event EventHandler<GridRect>? Edited;

    public bool IsStroking => _session is not null;

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world) return;
        // The Select, Prefabs, Trader, Spawn points and Town tools have their own controllers.
        if (_vm.SelectedTool is EditorTool.Select or EditorTool.Prefab or EditorTool.Trader or EditorTool.Spawn or EditorTool.Town) return;
        // No edits while the world is being saved or replaced; a stroke already under way may finish.
        if (_vm.IsBusy && e.Phase == ToolPhase.Down) return;
        var grid = new Point(e.MapPoint.X, world.Size - e.MapPoint.Y);

        // Ctrl+click samples the height under the cursor into the Flatten target (or the water level).
        if (e.Phase == ToolPhase.Down && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (_vm.SelectedTool == EditorTool.Water)
                SampleWaterLevel(world, grid);
            else if (world.Height.Contains((int)grid.X, (int)grid.Y))
            {
                _vm.FlattenHeight = SampleFlattenHeight(world, grid);
                _vm.Status = $"Flatten height set to {_vm.FlattenHeight:0.0} m";
            }
            return;
        }

        // Road clicks add points; Shift+drag with the road tool erases roads like a brush.
        if (_vm.SelectedTool == EditorTool.Road && e.Phase == ToolPhase.Down && !e.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (e.ClickCount >= 2) FinishRoad();
            else AddRoadPoint(world, grid, e.Modifiers);
            return;
        }

        switch (e.Phase)
        {
            case ToolPhase.Down:
                _erase = e.Modifiers.HasFlag(ModifierKeys.Shift);
                BeginStroke(world, grid);
                break;
            case ToolPhase.Move when _session is not null && _vm.SelectedTool is not (EditorTool.Stamp or EditorTool.Water):
                StrokeTo(world, grid);
                break;
            case ToolPhase.Up when _session is not null:
                EndStroke();
                break;
        }
    }

    private void BeginStroke(World world, Point grid)
    {
        var tool = _vm.SelectedTool;
        if (tool == EditorTool.Select) return;
        string label = _vm.Tools.First(t => t.Tool == tool).Label;
        if (_erase && tool == EditorTool.Radiation) label = "Erase radiation";
        if (_erase && tool == EditorTool.Water) label = "Drain water";
        if (tool == EditorTool.Road) label = "Erase road";
        _session = new EditSession(label);
        _noise = tool == EditorTool.Noise ? new Noise(Environment.TickCount) : null;

        if (tool == EditorTool.Flatten && !_vm.FlattenLocked && world.Height.Contains((int)grid.X, (int)grid.Y))
            _vm.FlattenHeight = SampleFlattenHeight(world, grid);

        if (tool == EditorTool.Stamp)
            PlaceStamp(world, grid);
        else if (tool == EditorTool.Water)
            FillWater(world, grid);
        else
            Dab(world, grid);
        _last = grid;
    }

    private void StrokeTo(World world, Point grid)
    {
        // Space dabs a quarter of the radius apart so fast drags still give an even stroke.
        double spacing = Math.Max(1, _vm.BrushRadius * 0.25);
        var delta = grid - _last;
        double length = delta.Length;
        if (length < spacing) return;
        int steps = (int)(length / spacing);
        for (int i = 1; i <= steps; i++)
            Dab(world, _last + delta * (i * spacing / length));
        _last += delta * (steps * spacing / length);
    }

    private void Dab(World world, Point grid)
    {
        var tip = _vm.BrushTip;
        switch (_vm.SelectedTool)
        {
            case EditorTool.Biome:
                if (_vm.SelectedBiome is { } biome)
                    _map.InvalidateRegion(LayerPaint.PaintBiome(world, (float)grid.X, (float)grid.Y, tip, biome.Color, _session!));
                return;
            case EditorTool.Radiation:
                _map.InvalidateRegion(LayerPaint.PaintRadiation(world, (float)grid.X, (float)grid.Y, tip, _erase, _session!));
                return;
            case EditorTool.Road:
                _map.InvalidateRegion(RoadOp.Erase(world, (float)grid.X, (float)grid.Y, tip, _session!));
                return;
        }

        var mode = _vm.SelectedTool switch
        {
            EditorTool.Raise => BrushMode.Raise,
            EditorTool.Lower => BrushMode.Lower,
            EditorTool.Flatten => BrushMode.Flatten,
            EditorTool.Smooth => BrushMode.Smooth,
            _ => BrushMode.Noise,
        };
        var settings = new BrushSettings(mode, tip.Radius, (float)_vm.BrushStrength,
            (float)_vm.FlattenHeight, NoiseScale: (float)_vm.NoiseScale, Shape: tip.Shape, Falloff: tip.Falloff,
            RotationDegrees: tip.RotationDegrees);
        var rect = TerrainBrush.Dab(world.Height, (float)grid.X, (float)grid.Y, settings, _session!, _noise);
        _map.InvalidateRegion(rect);
    }

    private void PlaceStamp(World world, Point grid)
    {
        if (_vm.SelectedStamp is not { } name)
        {
            _vm.Status = "Pick a stamp first (the game's stamps load from the game folder).";
            return;
        }
        var stamp = _vm.Stamps.Get(name);
        var placement = new StampPlacement((float)grid.X, (float)grid.Y, (float)_vm.StampSize, (float)_vm.StampRotation,
            (float)_vm.StampStrength, _vm.StampAdditive, ApplyWater: _vm.StampWater);
        var rect = StampOp.Apply(world, stamp, placement, _session!);
        _map.InvalidateRegion(rect);
    }

    private void FillWater(World world, Point grid)
    {
        int x = (int)grid.X, z = (int)grid.Y;
        if (!world.Height.Contains(x, z)) return;
        if (_erase)
        {
            var drained = WaterFill.Drain(world, x, z, _session!);
            _vm.Status = drained.Cells > 0 ? $"Drained {Area(drained.Cells)} of water." : "There is no water here to drain.";
            _map.InvalidateRegion(drained.Bounds);
            return;
        }
        byte level = (byte)Math.Clamp(Math.Round(_vm.WaterLevel), 1, 255);
        var filled = WaterFill.Fill(world, x, z, level, _session!);
        if (filled.Cells == 0)
            _vm.Status = $"The ground here is above {level} m; click lower ground or raise the water level.";
        else
            _vm.Status = $"Filled {Area(filled.Cells)} to {level} m."
                + (filled.Cells > (long)world.Size * world.Size / 4 ? " That flooded much of the map; Ctrl+Z undoes it." : "");
        _map.InvalidateRegion(filled.Bounds);
    }

    // ---- roads -------------------------------------------------------------

    public bool IsDrawingRoad => _roadPoints.Count > 0;

    private void AddRoadPoint(World world, Point grid, ModifierKeys modifiers)
    {
        if (!world.Height.Contains((int)grid.X, (int)grid.Y)) return;
        var (point, end) = SnapRoadPoint(world, new Vector2((float)grid.X, (float)grid.Y), modifiers);
        // Clicking the last point again adds nothing.
        if (_roadPoints.Count > 0 && Vector2.Distance(_roadPoints[^1], point) < 1) return;
        _roadPoints.Add(point);
        // Carrying a road on: take its surface and width, so the two join without a step.
        if (_roadPoints.Count == 1)
        {
            _startHeading = end?.Direction;
            if (end is not null) _vm.SelectedRoadPreset = new RoadPreset("", end.Type, MatchWidth(end));
        }
        UpdateRoadPreview();
        if (_roadPoints.Count == 1 && end is not null)
            _vm.Status = $"Carrying on a {_vm.RoadWidth:0.#} m {end.Type.ToString().ToLowerInvariant()} road. " + _vm.Status;
    }

    /// <summary>A measured road width, as the nearest preset of its surface when it is within a metre of one.</summary>
    private double MatchWidth(RoadEnd end)
    {
        double width = end.Radius * 2;
        var preset = _vm.RoadPresets.Where(p => p.Type == end.Type).MinBy(p => Math.Abs(p.Width - width));
        return preset is not null && Math.Abs(preset.Width - width) <= 1 ? preset.Width : Math.Round(width * 2) / 2;
    }

    /// <summary>Within this many degrees of north, south, east or west (or of the road being carried on), a segment snaps to it.</summary>
    private const double AngleSnapDegrees = 5;

    /// <summary>How near (in screen pixels) the mouse must be to a road's end to snap to it.</summary>
    private const double EndSnapPixels = 20;

    private Vector2? _startHeading; // the way the road being carried on was heading, if the first point snapped to its end

    /// <summary>
    /// Where a click at <paramref name="grid"/> puts the next road point: on a road end nearby, else with the segment
    /// from the last point turned onto a nearby compass direction (or the heading of the road being carried on).
    /// Alt turns snapping off.
    /// </summary>
    private (Vector2 Point, RoadEnd? End) SnapRoadPoint(World world, Vector2 grid, ModifierKeys modifiers)
    {
        if (modifiers.HasFlag(ModifierKeys.Alt)) return (grid, null);
        float reach = (float)Math.Clamp(EndSnapPixels / _map.Scale, 6, 60);
        if (world.Roads is { } roads && RoadEnds.Find(roads, grid, reach) is { } end) return (end.Point, end);
        if (_roadPoints.Count == 0) return (grid, null);

        var from = _roadPoints[^1];
        var d = grid - from;
        if (d.Length() < 1) return (grid, null);
        IEnumerable<Vector2> headings = [new(1, 0), new(0, 1), new(-1, 0), new(0, -1)];
        if (_roadPoints.Count == 1 && _startHeading is { } h) headings = headings.Prepend(h);
        double cos = Math.Cos(AngleSnapDegrees * Math.PI / 180);
        foreach (var heading in headings)
        {
            float along = Vector2.Dot(d, heading);
            if (along > 0 && along / d.Length() >= cos) return (from + heading * along, null);
        }
        return (grid, null);
    }

    /// <summary>Where the road tool would put a point for the mouse at <paramref name="mapPoint"/> (map coordinates), for the preview.</summary>
    public RoadSnap? SnapCursor(Point mapPoint)
    {
        if (World is not { } world || _vm.SelectedTool != EditorTool.Road) return null;
        var grid = new Vector2((float)mapPoint.X, (float)(world.Size - mapPoint.Y));
        var (point, end) = SnapRoadPoint(world, grid, Keyboard.Modifiers);
        return new RoadSnap(new Point(point.X, world.Size - point.Y), end is not null);
    }

    /// <summary>Removes the last point of the road being drawn.</summary>
    public void UndoRoadPoint()
    {
        if (_roadPoints.Count == 0) return;
        _roadPoints.RemoveAt(_roadPoints.Count - 1);
        if (_roadPoints.Count == 0) _startHeading = null;
        UpdateRoadPreview();
    }

    public void CancelRoad()
    {
        if (_roadPoints.Count == 0) return;
        _roadPoints.Clear();
        _startHeading = null;
        UpdateRoadPreview();
        _vm.Status = "Road cancelled.";
    }

    /// <summary>Draws the road being edited into the world as one undo step.</summary>
    public void FinishRoad()
    {
        if (World is not { } world || _roadPoints.Count == 0) return;
        if (_roadPoints.Count < 2)
        {
            _vm.Status = "A road needs at least two points.";
            return;
        }
        var road = new RoadPath([.. _roadPoints], _vm.RoadType, (float)_vm.RoadWidth / 2, _vm.RoadGrade);
        var session = new EditSession(_vm.RoadType == RoadType.Asphalt ? "Asphalt road" : "Gravel road");
        var rect = RoadOp.Draw(world, road, session);
        float length = RoadOp.Length(_roadPoints);
        _roadPoints.Clear();
        _startHeading = null;
        UpdateRoadPreview();
        _map.InvalidateRegion(rect);
        _vm.Status = $"Drew {length:N0} m of {_vm.RoadType.ToString().ToLowerInvariant()} road.";
        if (!session.HasChanges) return;
        _undo.Push(session);
        Edited?.Invoke(this, session.Bounds);
    }

    private void UpdateRoadPreview()
    {
        if (World is not { } world || _roadPoints.Count == 0)
        {
            _map.RoadPreview = null;
            return;
        }
        _map.RoadPreview = new RoadPreview(
            [.. _roadPoints.Select(p => new Point(p.X, world.Size - p.Y))], _vm.RoadWidth, _vm.RoadType == RoadType.Asphalt);
        _vm.Status = $"{_roadPoints.Count} points, {RoadOp.Length(_roadPoints):N0} m. Double-click or Enter to finish, Backspace removes a point, Esc cancels.";
    }

    /// <summary>Refreshes the preview after the road type or width changes.</summary>
    public void RefreshRoadPreview()
    {
        if (_roadPoints.Count > 0) UpdateRoadPreview();
    }

    /// <summary>The ground height under <paramref name="grid"/> as a Flatten target: whole metres if asked, else to 0.1 m.</summary>
    private double SampleFlattenHeight(World world, Point grid)
    {
        double h = World.HeightToMetres(world.Height[(int)grid.X, (int)grid.Y]);
        return _vm.FlattenWholeMetres ? Math.Round(h, MidpointRounding.AwayFromZero) : Math.Round(h, 1);
    }

    private void SampleWaterLevel(World world, Point grid)
    {
        int x = (int)grid.X, z = (int)grid.Y;
        if (!world.Height.Contains(x, z)) return;
        byte surface = world.Water?[x, z] ?? 0;
        float ground = World.HeightToMetres(world.Height[x, z]);
        // On existing water take its surface; on land, the level that just reaches this point.
        _vm.WaterLevel = surface > 0 && ground <= surface + 0.5f ? surface : Math.Min(255, Math.Ceiling(ground) + 1);
        _vm.Status = $"Water level set to {_vm.WaterLevel:0} m";
    }

    private static string Area(long cells) =>
        cells >= 1_000_000 ? $"{cells / 1e6:0.00} km²" : $"{cells:N0} m²";

    private void EndStroke()
    {
        var session = _session!;
        _session = null;
        _noise = null;
        if (!session.HasChanges) return;
        _undo.Push(session);
        Edited?.Invoke(this, session.Bounds);
    }
}
