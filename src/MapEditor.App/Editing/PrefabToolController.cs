using System.Windows;
using System.Windows.Input;
using MapEditor.App.Map;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.App.Editing;

/// <summary>
/// The Prefabs and Spawn points tools. With a prefab picked in the browser, clicks place it; otherwise clicks
/// select prefabs and drags move them. Spawn clicks add, select and drag spawns. Each change is one undo step.
/// </summary>
public sealed class PrefabToolController
{
    private readonly MapView _map;
    private readonly MainViewModel _vm;
    private readonly UndoStack _undo;

    private PrefabInstance? _selected;
    private SpawnPoint? _selectedSpawn;
    private Point? _hover; // grid metres (x east, z north) under the mouse

    // Prefab drag: the prefab, where it was grabbed relative to its footprint centre, and where it would land.
    private PrefabInstance? _dragging;
    private Vector _grab;
    private PrefabSpot? _dropSpot;
    private List<PrefabInstance> _dropCovered = []; // POIs the dragged one covers and replaces on drop
    private bool _dropInLot;

    // Spawn drag, moved live inside one undo step.
    private EditSession? _spawnSession;

    public PrefabToolController(MapView map, MainViewModel vm, UndoStack undo)
    {
        _map = map;
        _vm = vm;
        _undo = undo;
        _map.ToolInput += OnToolInput;
        _map.CursorMoved += (_, m) =>
        {
            _hover = m is { } p && World is { } w ? new Point(p.X, w.Size - p.Y) : null;
            if (_dragging is null) UpdateGhost();
            // A hand over what the Select tool can pick up.
            if (_vm.IsSelectTool && !IsDragging && !_map.IsPanning)
            {
                var cursor = m is { } at && CanGrab(at) ? Cursors.Hand : null;
                if (_map.Cursor != cursor) _map.Cursor = cursor;
            }
        };
    }

    public World? World { get; set; }

    private PrefabLibrary Library => _vm.Prefabs;

    /// <summary>Raised after an edit has been committed to the undo history.</summary>
    public event EventHandler<GridRect>? Edited;

    public bool IsDragging => _dragging is not null || _spawnSession is not null;

    private bool Armed => _vm.PlacingPrefab is not null;

    private void OnToolInput(object? sender, ToolInputEventArgs e)
    {
        if (World is not { } world) return;
        if (_vm.IsBusy && e.Phase == ToolPhase.Down) return;
        var grid = new Point(e.MapPoint.X, world.Size - e.MapPoint.Y);
        if (_vm.IsPrefabTool) PrefabInput(world, e, grid);
        else if (_vm.SelectedTool == EditorTool.Spawn) SpawnInput(world, e, grid);
        else if (_vm.IsSelectTool) SelectInput(world, e, grid);
    }

    // ---- prefabs -----------------------------------------------------------

    private void PrefabInput(World world, ToolInputEventArgs e, Point grid)
    {
        switch (e.Phase)
        {
            case ToolPhase.Down when e.Modifiers.HasFlag(ModifierKeys.Shift):
                if (WorldQuery.PrefabAt(world, world.GridToWorld((int)grid.X), world.GridToWorld((int)grid.Y), Library) is { } hit)
                    Delete(world, hit);
                break;
            case ToolPhase.Down when Armed:
                Place(world, _vm.PlacingPrefab!);
                break;
            case ToolPhase.Down:
                SelectAndGrab(world, WorldQuery.PrefabAt(world, world.GridToWorld((int)grid.X), world.GridToWorld((int)grid.Y), Library), grid);
                break;
            case ToolPhase.Move when _dragging is not null:
                DragTo(world, grid);
                break;
            case ToolPhase.Up when _dragging is not null:
                EndDrag(world);
                break;
        }
    }

    /// <summary>Selects a prefab and, if it is in the game folder, picks it up to be dragged.</summary>
    private void SelectAndGrab(World world, PrefabInstance? under, Point grid)
    {
        Select(under);
        if (under is null || Library.Find(under.Name) is not { } info) return;
        var (sx, sz) = info.Footprint(under.Rotation);
        var centre = new Point(world.WorldToGrid(under.Position.X) + sx / 2.0, world.WorldToGrid(under.Position.Z) + sz / 2.0);
        _dragging = under;
        _grab = grid - centre;
        _dropSpot = null;
    }

    // ---- the Select tool ---------------------------------------------------

    public bool HasSpawnSelected => _selectedSpawn is not null;

    /// <summary>What the Select tool picks at a point: POIs, traders and street tiles (the smallest under it), not parts.</summary>
    private PrefabInstance? PickAt(World world, Point grid) =>
        WorldQuery.PrefabAt(world, world.GridToWorld((int)grid.X), world.GridToWorld((int)grid.Y), Library,
            info => info is not { IsPart: true });

    private SpawnPoint? SpawnNear(World world, Point grid) =>
        SpawnHelper.Near(world, (float)grid.X, (float)grid.Y, (float)Math.Max(4, 8 / _map.Scale));

    /// <summary>Whether the Select tool would pick something up at a map point (else a drag there pans).</summary>
    public bool CanGrab(Point mapPoint)
    {
        if (World is not { } world) return false;
        var grid = new Point(mapPoint.X, world.Size - mapPoint.Y);
        return SpawnNear(world, grid) is not null || PickAt(world, grid) is not null;
    }

    /// <summary>The Select tool: a press on a spawn point or prefab selects it, and dragging moves it.</summary>
    private void SelectInput(World world, ToolInputEventArgs e, Point grid)
    {
        switch (e.Phase)
        {
            case ToolPhase.Down when SpawnNear(world, grid) is { } spawn:
                Select(null);
                SelectSpawn(spawn);
                _spawnSession = new EditSession("Move spawn");
                break;
            case ToolPhase.Down:
                SelectSpawn(null);
                SelectAndGrab(world, PickAt(world, grid), grid);
                break;
            case ToolPhase.Move when _spawnSession is not null && _selectedSpawn is { } moving:
                if (!world.Height.Contains((int)grid.X, (int)grid.Y)) return;
                SpawnHelper.Move(world, moving, (int)grid.X, (int)grid.Y, _spawnSession);
                SelectSpawn(moving);
                break;
            case ToolPhase.Up when _spawnSession is not null:
                Commit(_spawnSession);
                _spawnSession = null;
                break;
            case ToolPhase.Move when _dragging is not null:
                DragTo(world, grid);
                break;
            case ToolPhase.Up when _dragging is not null:
                EndDrag(world);
                break;
        }
    }

    private void Place(World world, PrefabInfo info)
    {
        if (_hover is not { } at) return;
        var spot = PrefabPlacer.SpotAt(world, Library, info, (float)at.X, (float)at.Y, _vm.PrefabRotation, out var lot);
        spot = Snapped(world, info, spot, lot, null);
        bool inLot = lot is { Fits: true };
        var covered = PrefabPlacer.Covered(world, Library, info, spot);
        var problems = PrefabPlacer.Check(world, Library, info, spot, covered, snappedToLot: inLot);
        var session = new EditSession("Place " + info.Name);
        foreach (var old in covered) PrefabPlacer.Delete(world, Library, old, session);
        var placed = PrefabPlacer.Place(world, info, spot, _vm.PrefabFlatten, session, inLot);
        int parts = AddParts(world, info, spot, inLot ? lot!.Tile : null);
        Commit(session);
        Select(placed);
        _vm.Status = $"Placed {info.Name} at {Where(spot)}"
            + (inLot ? $", in a {lot!.Lot.Size.X}×{lot.Lot.Size.Z} m lot" : "")
            + (parts > 0 ? $", with {parts} parts" : "")
            + Replacing(covered)
            + (problems.Count > 0 ? ". Warning: " + string.Join(", ", problems) : ".");
        UpdateGhost();
    }

    /// <summary>Adds the parts from the prefab's markers if asked; returns how many.</summary>
    private int AddParts(World world, PrefabInfo info, PrefabSpot spot, PrefabInstance? lotTile)
    {
        if (!_vm.PrefabAddParts) return 0;
        // RWG filters tagged part markers by the district: the lot's tile, the tile itself, or the wilderness.
        string? district = lotTile is not null ? Library.Find(lotTile.Name)?.District : info.IsStreetTile ? info.District : "wilderness";
        var parts = PrefabPlacer.MarkerParts(Library, info, spot, Environment.TickCount, district);
        world.Prefabs.AddRange(parts);
        return parts.Count;
    }

    /// <summary>Swaps the selected prefab for the one picked in the browser, in the same place and facing.</summary>
    public void ReplaceSelected()
    {
        if (World is not { } world || _selected is not { } old) return;
        if (_vm.SelectedPrefabInfo is not { } info)
        {
            _vm.Status = "Pick the replacement in the prefab list first.";
            return;
        }
        if (info.IsStreetTile != (Library.Find(old.Name)?.IsStreetTile ?? false))
        {
            _vm.Status = "Street tiles can only be swapped for street tiles, and POIs for POIs.";
            return;
        }
        var session = new EditSession($"Replace {old.Name}");
        var (placed, lot) = PrefabPlacer.Replace(world, Library, old, info, _vm.PrefabFlatten, session);
        AddParts(world, info, new PrefabSpot(placed.Position.X, placed.Position.Y, placed.Position.Z, placed.Rotation), lot?.Tile);
        Commit(session);
        Select(placed);
        // Stop placing, so the next click selects instead of dropping another copy.
        _vm.SelectedPrefabInfo = null;
        _vm.Status = $"Replaced {old.Name} with {info.Name}.";
    }

    /// <summary>Page Up and Page Down: raises or lowers the selected prefab (and what is on it) by whole blocks.</summary>
    public void NudgeHeight(int blocks)
    {
        if (World is not { } world || _selected is not { } p || Library.Find(p.Name) is null) return;
        var spot = new PrefabSpot(p.Position.X, p.Position.Y + blocks, p.Position.Z, p.Rotation);
        var session = new EditSession(blocks > 0 ? $"Raise {p.Name}" : $"Lower {p.Name}");
        // The ground stays as it is; the game copies the prefab's own terrain in on load.
        PrefabPlacer.Move(world, Library, p, spot, flatten: false, session);
        Commit(session);
        Select(p);
        _vm.Status = $"{p.Name} is now at y {p.Position.Y}.";
    }

    private void DragTo(World world, Point grid)
    {
        var p = _dragging!;
        if (Library.Find(p.Name) is not { } info) return;
        var centre = grid - _grab;
        var ignore = PrefabPlacer.Contents(world, Library, p);
        ignore.Add(p);
        var spot = PrefabPlacer.SpotAt(world, Library, info, (float)centre.X, (float)centre.Y, p.Rotation, out var lot, ignore);
        spot = Snapped(world, info, spot, lot, ignore);
        _dropInLot = lot is { Fits: true };
        _dropSpot = spot;
        _dropCovered = PrefabPlacer.Covered(world, Library, info, spot, ignore);
        var problems = PrefabPlacer.Check(world, Library, info, spot, [.. ignore, .. _dropCovered], _dropInLot);
        _map.PrefabGhost = Ghost(info, spot, problems.Count > 0, lot, _dropCovered);
        _vm.PlacementText = Describe(info, spot, problems, lot) + Replacing(_dropCovered, "\n");
    }

    private void EndDrag(World world)
    {
        var p = _dragging!;
        _dragging = null;
        _map.PrefabGhost = null;
        if (_dropSpot is not { } spot || spot.Position == p.Position && spot.Rotation == p.Rotation)
        {
            Select(p);
            return;
        }
        MoveTo(world, p, spot, _dropInLot, "Move " + p.Name, _dropCovered);
        _dropCovered = [];
    }

    private void MoveTo(World world, PrefabInstance p, PrefabSpot spot, bool inLot, string label, List<PrefabInstance>? covered = null)
    {
        var session = new EditSession(label);
        foreach (var old in covered ?? []) PrefabPlacer.Delete(world, Library, old, session);
        PrefabPlacer.Move(world, Library, p, spot, _vm.PrefabFlatten, session, inLot);
        Commit(session);
        Select(p);
        _vm.Status = $"Moved {p.Name} to {Where(spot)}{Replacing(covered ?? [])}.";
    }

    /// <summary>How near (in screen pixels) a POI's edge must come to a street tile's edge to snap to it.</summary>
    private const double EdgeSnapPixels = 10;

    /// <summary>
    /// A POI's spot with its edges snapped to street tiles nearby, unless it sits in a street-tile lot (which places it) or
    /// Alt is held.
    /// </summary>
    private PrefabSpot Snapped(World world, PrefabInfo info, PrefabSpot spot, LotSnap? lot, IReadOnlyCollection<PrefabInstance>? ignore)
    {
        if (lot is { Fits: true } || Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return spot;
        int snap = (int)Math.Clamp(Math.Round(EdgeSnapPixels / _map.Scale), 1, 20);
        return PrefabPlacer.SnapToTiles(world, Library, info, spot, snap, ignore);
    }

    private static string Replacing(List<PrefabInstance> covered, string lead = ", ") =>
        covered.Count == 0 ? "" : lead + "replacing " + string.Join(", ", covered.Select(p => p.Name));

    private void Delete(World world, PrefabInstance p)
    {
        var session = new EditSession("Delete " + p.Name);
        int removed = PrefabPlacer.Delete(world, Library, p, session);
        Commit(session);
        if (ReferenceEquals(_selected, p) || !world.Prefabs.Contains(_selected!)) Select(null);
        _vm.Status = removed > 1 ? $"Deleted {p.Name} and the {removed - 1} prefabs on it." : $"Deleted {p.Name}.";
    }

    /// <summary>Q and E: turn the prefab being placed, or the selected one, a quarter turn (Q counter-clockwise).</summary>
    public void Rotate(int quarterTurns)
    {
        if (World is not { } world) return;
        if (Armed)
        {
            _vm.PrefabRotation = (_vm.PrefabRotation + quarterTurns) & 3;
            UpdateGhost();
            return;
        }
        if (_selected is not { } p || Library.Find(p.Name) is not { } info)
        {
            if (_selected is not null) _vm.Status = $"{_selected.Name} is not in the game folder's prefabs, so it can't be turned.";
            return;
        }
        // Turn about the footprint centre; street tiles are square and stay on their grid cell.
        var (sx, sz) = info.Footprint(p.Rotation);
        var centre = new Point(world.WorldToGrid(p.Position.X) + sx / 2.0, world.WorldToGrid(p.Position.Z) + sz / 2.0);
        var ignore = PrefabPlacer.Contents(world, Library, p);
        ignore.Add(p);
        var spot = PrefabPlacer.SpotAt(world, Library, info, (float)centre.X, (float)centre.Y, p.Rotation + quarterTurns, out var lot, ignore);
        if (lot is { Fits: true } && spot.Rotation == p.Rotation)
        {
            _vm.Status = "This prefab is in a street-tile lot, which sets its rotation so it faces the street.";
            return;
        }
        MoveTo(world, p, spot, lot is { Fits: true }, "Rotate " + p.Name);
    }

    /// <summary>Delete key: removes the selected prefab or spawn.</summary>
    public void DeleteSelected()
    {
        if (World is not { } world) return;
        if ((_vm.SelectedTool == EditorTool.Spawn || _vm.IsSelectTool) && _selectedSpawn is { } spawn)
        {
            var session = new EditSession("Delete spawn");
            SpawnHelper.Delete(world, spawn, session);
            Commit(session);
            SelectSpawn(null);
            _vm.Status = "Deleted the spawn point.";
        }
        else if ((_vm.IsPrefabTool || _vm.IsSelectTool) && _selected is { } p)
        {
            Delete(world, p);
        }
    }

    public void Deselect()
    {
        Select(null);
        SelectSpawn(null);
    }

    /// <summary>Esc: stops placing, else clears the selection. Returns false if there was nothing to cancel.</summary>
    public bool Cancel()
    {
        if (_vm.SelectedTool == EditorTool.Prefab && Armed)
        {
            _vm.SelectedPrefabInfo = null;
            return true;
        }
        if (_selected is null && _selectedSpawn is null) return false;
        Select(null);
        SelectSpawn(null);
        return true;
    }

    // ---- spawns ------------------------------------------------------------

    private void SpawnInput(World world, ToolInputEventArgs e, Point grid)
    {
        float pick = (float)Math.Max(4, 8 / _map.Scale);
        switch (e.Phase)
        {
            case ToolPhase.Down:
                var near = SpawnHelper.Near(world, (float)grid.X, (float)grid.Y, pick);
                if (e.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    if (near is null) return;
                    _selectedSpawn = near;
                    DeleteSelected();
                    return;
                }
                if (!world.Height.Contains((int)grid.X, (int)grid.Y)) return;
                if (near is not null)
                {
                    SelectSpawn(near);
                    _spawnSession = new EditSession("Move spawn");
                    return;
                }
                var session = new EditSession("Add spawn");
                var spawn = SpawnHelper.Add(world, (int)grid.X, (int)grid.Y, 0, session);
                Commit(session);
                SelectSpawn(spawn);
                _vm.Status = $"Added a spawn point; the world has {world.Spawns.Count}. RWG makes 12.";
                break;
            case ToolPhase.Move when _spawnSession is not null && _selectedSpawn is { } moving:
                if (!world.Height.Contains((int)grid.X, (int)grid.Y)) return;
                SpawnHelper.Move(world, moving, (int)grid.X, (int)grid.Y, _spawnSession);
                SelectSpawn(moving);
                break;
            case ToolPhase.Up when _spawnSession is not null:
                Commit(_spawnSession);
                _spawnSession = null;
                break;
        }
    }

    /// <summary>Q and E with the spawn tool: turn the selected spawn 45 degrees (Q counter-clockwise).</summary>
    public void RotateSpawn(float degrees)
    {
        if (World is not { } world || _selectedSpawn is not { } spawn) return;
        var session = new EditSession("Rotate spawn");
        SpawnHelper.Rotate(world, spawn, degrees, session);
        Commit(session);
        SelectSpawn(spawn);
    }

    // ---- selection and display ----------------------------------------------

    private void Select(PrefabInstance? p)
    {
        _selected = p;
        _map.SelectedPrefab = p;
        _vm.SelectedMapPrefab = p is null ? null : Library.Find(p.Name);
        _vm.SelectedMapPrefabName = p?.Name ?? "";
        _vm.HasPrefabSelection = p is not null;
        if (p is null)
        {
            _vm.SelectionText = "";
            return;
        }
        var info = Library.Find(p.Name);
        if (info is null || World is not { } world)
        {
            _vm.SelectionText = "Not in the game folder's prefabs, so it can only be deleted.";
            return;
        }
        var spot = new PrefabSpot(p.Position.X, p.Position.Y, p.Position.Z, p.Rotation);
        var ignore = PrefabPlacer.Contents(world, Library, p);
        ignore.Add(p);
        // Ground under a placed prefab was flattened (or is a lot), so only overlaps and water matter now.
        var problems = PrefabPlacer.Check(world, Library, info, spot, ignore, snappedToLot: true);
        string contents = ignore.Count > 1 ? $"\n{ignore.Count - 1} prefabs on it move with it." : "";
        string tags = info.Tags.Count > 0 ? "\nTags: " + string.Join(", ", info.Tags) : "";
        string zoning = info.Zoning.Count > 0 ? "\nZoning: " + string.Join(", ", info.Zoning) : "";
        _vm.SelectionText = Describe(info, spot, problems, null, withName: false) + tags + zoning + contents;
    }

    private void SelectSpawn(SpawnPoint? spawn)
    {
        _selectedSpawn = spawn;
        _map.SelectedSpawn = spawn;
        _vm.SelectionText = spawn is null ? "" : $"Spawn at {spawn.X:0} E, {spawn.Z:0} N, y {spawn.Y:0}\nFacing {spawn.RotationY:0}° (Q and E turn it)";
    }

    /// <summary>Redraws the placement ghost under the mouse while a prefab is picked in the browser.</summary>
    public void UpdateGhost()
    {
        if (World is not { } world || !_vm.IsPrefabTool || _vm.PlacingPrefab is not { } info || _hover is not { } at)
        {
            _map.PrefabGhost = null;
            if (_vm.SelectedTool == EditorTool.Prefab && !Armed)
                _vm.PlacementText = "Pick a prefab below to place it, or click one on the map to select it.";
            else if (_vm.IsTraderTool && !Armed)
                _vm.PlacementText = "No trader prefabs found. Set the game folder under Settings.";
            return;
        }
        var spot = PrefabPlacer.SpotAt(world, Library, info, (float)at.X, (float)at.Y, _vm.PrefabRotation, out var lot);
        spot = Snapped(world, info, spot, lot, null);
        var covered = PrefabPlacer.Covered(world, Library, info, spot);
        var problems = PrefabPlacer.Check(world, Library, info, spot, covered, snappedToLot: lot is { Fits: true });
        _map.PrefabGhost = Ghost(info, spot, problems.Count > 0, lot, covered);
        _vm.PlacementText = Describe(info, spot, problems, lot) + Replacing(covered, "\n");
    }

    private PrefabGhost Ghost(PrefabInfo info, PrefabSpot spot, bool warning, LotSnap? lot, List<PrefabInstance> covered)
    {
        var (sx, sz) = info.Footprint(spot.Rotation);
        Rect? lotRect = lot is null ? null : _map.FootprintMapRect(lot.LotX, lot.LotZ, lot.Lot.Size.X, lot.Lot.Size.Z);
        var replaces = covered.Select(p =>
        {
            var (px, pz) = Library.Find(p.Name)!.Footprint(p.Rotation);
            return _map.FootprintMapRect(p.Position.X, p.Position.Z, px, pz);
        }).ToList();
        return new PrefabGhost(_map.FootprintMapRect(spot.X, spot.Z, sx, sz), info.FrontDirection(spot.Rotation), warning,
            lotRect, lot?.Fits ?? true, info, spot.Rotation) { Replaces = replaces };
    }

    private static readonly string[] Facing = ["north", "west", "south", "east"];

    private static string Describe(PrefabInfo info, PrefabSpot spot, List<string> problems, LotSnap? lot, bool withName = true)
    {
        var (sx, sz) = info.Footprint(spot.Rotation);
        string text = (withName ? info.Name + "\n" : "") + $"{sx}×{sz} m, tier {info.DifficultyTier}, front faces {Facing[info.FrontDirection(spot.Rotation)]}\n"
                      + $"at {Where(spot)}, rotation {spot.Rotation}";
        if (lot is not null)
            text += lot.Fits ? $"\nIn a {lot.Lot.Size.X}×{lot.Lot.Size.Z} m lot, facing its street" : $"\nToo big for this {lot.Lot.Size.X}×{lot.Lot.Size.Z} m lot";
        if (problems.Count > 0) text += "\nWarning: " + string.Join(", ", problems);
        return text;
    }

    private static string Where(PrefabSpot s) =>
        $"{(s.X >= 0 ? $"{s.X} E" : $"{-s.X} W")}, {(s.Z >= 0 ? $"{s.Z} N" : $"{-s.Z} S")}, y {s.Y}";

    /// <summary>After undo/redo or a new world: drops selections that are no longer in the world.</summary>
    public void Refresh()
    {
        _dragging = null;
        _spawnSession = null;
        if (World is not { } world || _selected is not null && !world.Prefabs.Contains(_selected)) _selected = null;
        if (World is null || _selectedSpawn is not null && !World.Spawns.Contains(_selectedSpawn)) _selectedSpawn = null;
        // Each tool shows only its own selection.
        _map.SelectedPrefab = null;
        _map.SelectedSpawn = null;
        _vm.SelectionText = "";
        _vm.HasPrefabSelection = false;
        if (_vm.IsPrefabTool) Select(_selected);
        else if (_vm.SelectedTool == EditorTool.Spawn) SelectSpawn(_selectedSpawn);
        else if (_vm.IsSelectTool && _selectedSpawn is not null) SelectSpawn(_selectedSpawn);
        else if (_vm.IsSelectTool) Select(_selected);
        if (!_vm.IsSelectTool && !_map.IsPanning && _map.Cursor == Cursors.Hand) _map.Cursor = null;
        UpdateGhost();
        _map.InvalidateVisual();
    }

    private void Commit(EditSession session)
    {
        _map.InvalidateRegion(session.Bounds);
        _map.InvalidateVisual();
        if (!session.HasChanges) return;
        _undo.Push(session);
        Edited?.Invoke(this, session.Bounds);
    }
}
