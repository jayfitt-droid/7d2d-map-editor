using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>Turns tool input on the map into terrain edits, one undo step per stroke.</summary>
public sealed class TerrainToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly UndoStack _undo;
    private EditSession? _session;
    private Point _last; // grid coords (x east, z north) of the previous dab
    private Noise? _noise;

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
        // No edits while the world is being saved or replaced; a stroke already under way may finish.
        if (_vm.IsBusy && e.Phase == ToolPhase.Down) return;
        var grid = new Point(e.MapPoint.X, world.Size - e.MapPoint.Y);

        // Ctrl+click samples the height under the cursor into the Flatten target, whatever the tool.
        if (e.Phase == ToolPhase.Down && e.Modifiers.HasFlag(ModifierKeys.Control))
        {
            if (world.Height.Contains((int)grid.X, (int)grid.Y))
            {
                _vm.FlattenHeight = Math.Round(World.HeightToMetres(world.Height[(int)grid.X, (int)grid.Y]), 1);
                _vm.Status = $"Flatten height set to {_vm.FlattenHeight:0.0} m";
            }
            return;
        }

        switch (e.Phase)
        {
            case ToolPhase.Down:
                BeginStroke(world, grid);
                break;
            case ToolPhase.Move when _session is not null && _vm.SelectedTool != EditorTool.Stamp:
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
        if (tool == EditorTool.Pan) return;
        _session = new EditSession(_vm.Tools.First(t => t.Tool == tool).Label);
        _noise = tool == EditorTool.Noise ? new Noise(Environment.TickCount) : null;

        if (tool == EditorTool.Flatten && !_vm.FlattenLocked && world.Height.Contains((int)grid.X, (int)grid.Y))
            _vm.FlattenHeight = Math.Round(World.HeightToMetres(world.Height[(int)grid.X, (int)grid.Y]), 1);

        if (tool == EditorTool.Stamp)
            PlaceStamp(world, grid);
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
        var mode = _vm.SelectedTool switch
        {
            EditorTool.Raise => BrushMode.Raise,
            EditorTool.Lower => BrushMode.Lower,
            EditorTool.Flatten => BrushMode.Flatten,
            EditorTool.Smooth => BrushMode.Smooth,
            _ => BrushMode.Noise,
        };
        var settings = new BrushSettings(mode, (float)_vm.BrushRadius, (float)_vm.BrushStrength,
            (float)_vm.FlattenHeight, NoiseScale: (float)_vm.NoiseScale);
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
