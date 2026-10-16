using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class PrefabTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-prefab-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World Flat(int size = 1024, float metres = 40)
    {
        var world = new World { Name = "P", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        return world;
    }

    private static float H(World w, int x, int z) => World.HeightToMetres(w.Height[x, z]);

    private static PrefabInfo Poi(string name, int sx, int sz, int yOffset = -1, int faceNorth = 0) =>
        new(name, "POIs", new Int3(sx, 20, sz), yOffset, faceNorth, 1, ["rural"], null);

    /// <summary>A 150 m street tile with one 40 x 30 lot in its south-west corner whose street is to the north.</summary>
    private static PrefabInfo Tile(int lotRotations = 0) =>
        new("rwg_tile_residential_straight", "RWGTiles", new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null)
        {
            Markers =
            [
                new PrefabMarker(new Int3(10, 12, 20), new Int3(40, 0, 30), MarkerType.POISpawn, "1", Rotations: lotRotations),
                new PrefabMarker(new Int3(70, 12, 70), new Int3(1, 1, 1), MarkerType.PartSpawn, "light", Part: "part_light"),
            ],
        };

    [Fact]
    public void Library_reads_markers_and_zoning_from_the_sidecar()
    {
        string xml = Path.Combine(_dir, "rwg_tile_downtown_cap.xml");
        File.WriteAllText(xml, """
            <prefab>
              <property name="PrefabSize" value="150, 22, 150" />
              <property name="YOffset" value="-12" />
              <property name="Zoning" value="downtown, commercial" />
              <property name="Tags" value="streettile, rwgonly" />
              <property name="POIMarkerSize" value="25, 0, 25#1, 1, 1" />
              <property name="POIMarkerStart" value="43, 12, 26#68, 18, 58" />
              <property name="POIMarkerGroup" value="1,light1" />
              <property name="POIMarkerTags" value="#" />
              <property name="POIMarkerType" value="POISpawn,PartSpawn" />
              <property name="POIMarkerPartToSpawn" value=",part_trafficlight_single" />
              <property name="POIMarkerPartRotations" value="3,1" />
              <property name="POIMarkerPartSpawnChance" value="1,0.2" />
            </prefab>
            """);
        File.WriteAllText(Path.ChangeExtension(xml, ".tts"), "");

        var info = PrefabLibrary.Load([_dir]).Find("rwg_tile_downtown_cap")!;
        Assert.True(info.IsStreetTile);
        Assert.Equal("downtown", info.District);
        Assert.Equal(["downtown", "commercial"], info.Zoning);
        Assert.Equal(2, info.Markers.Count);
        Assert.Equal(new PrefabMarker(new Int3(43, 12, 26), new Int3(25, 0, 25), MarkerType.POISpawn, "1", "", 3, 1, null), info.Markers[0]);
        Assert.Equal(MarkerType.PartSpawn, info.Markers[1].Type);
        Assert.Equal("part_trafficlight_single", info.Markers[1].Part);
        Assert.Equal(0.2f, info.Markers[1].PartChance);
    }

    [Fact]
    public void Markers_turn_counter_clockwise_with_the_prefab()
    {
        // A 1 x 1 marker on the middle of the north edge of a 10 (x) by 20 (z) prefab.
        var info = Poi("p", 10, 20) with { Markers = [new PrefabMarker(new Int3(5, 0, 19), new Int3(1, 0, 1), MarkerType.POISpawn)] };
        // One turn: the prefab is 20 x 10 and north has gone to the west edge.
        Assert.Equal(new Int3(0, 0, 5), info.RotatedMarkers(1)[0].Start);
        // Two turns: the south edge, mirrored.
        Assert.Equal(new Int3(4, 0, 0), info.RotatedMarkers(2)[0].Start);
        Assert.Equal(info.Markers[0], info.RotatedMarkers(4)[0]);
        Assert.Equal(1, info.FrontDirection(1));
    }

    [Fact]
    public void Median_ground_matches_rwg()
    {
        var world = Flat();
        for (int x = 100; x < 110; x++) world.Height[x, 100] = World.MetresToHeight(49.5f); // rounds up to 50
        // Even count: the two middle values are averaged.
        Assert.Equal(45, PrefabPlacer.MedianGround(world, new GridRect(100, 100, 110, 102)));
        Assert.Equal(40, PrefabPlacer.MedianGround(world, new GridRect(100, 100, 110, 103)));
    }

    [Fact]
    public void Placing_flattens_the_footprint_and_blends_out_over_18_m()
    {
        var world = Flat(metres: 40);
        var info = Poi("cabin", 20, 20, yOffset: -2);
        // A hill under one corner of the footprint.
        for (int z = 500; z < 512; z++)
            for (int x = 500; x < 512; x++)
                world.Height[x, z] = World.MetresToHeight(52);
        var session = new EditSession("Place");
        var spot = PrefabPlacer.SpotAt(world, PrefabLibrary.Of([info]), info, 510, 510, 0, out var lot);
        Assert.Null(lot);
        Assert.Equal(new PrefabSpot(-12, 39, -12, 0), spot); // median ground 40 + 1 + YOffset -2
        PrefabPlacer.Place(world, info, spot, flatten: true, session);

        Assert.Single(world.Prefabs);
        Assert.Equal(40, H(world, 505, 505), 1);
        Assert.Equal(40, H(world, 519, 519), 1);
        Assert.True(session.HasChanges);
    }

    [Fact]
    public void Flatten_blends_with_a_smoothstep()
    {
        var world = Flat(metres: 60);
        var session = new EditSession("Flatten");
        PrefabPlacer.Flatten(world, new GridRect(400, 400, 420, 420), 40, 18, session);
        Assert.Equal(40, H(world, 419, 410), 1);
        Assert.Equal(50, H(world, 428, 410), 1); // half way out: smoothstep(0.5) = 0.5
        Assert.Equal(60, H(world, 438, 410), 1);
        Assert.InRange(H(world, 422, 410), 40, 42);
    }

    [Fact]
    public void Placing_and_undoing_restores_the_prefab_list_and_ground()
    {
        var world = Flat();
        world.Height[300, 300] = World.MetresToHeight(70);
        var info = Poi("cabin", 10, 10);
        var undo = new UndoStack();
        var session = new EditSession("Place");
        PrefabPlacer.Place(world, info, new PrefabSpot(-215, 40, -215, 0), flatten: true, session); // ground 40 + 1 + YOffset -1
        undo.Push(session);
        Assert.Single(world.Prefabs);
        Assert.Equal(40, H(world, 300, 300), 1);

        undo.Undo();
        Assert.Empty(world.Prefabs);
        Assert.Equal(70, H(world, 300, 300), 1);

        undo.Redo();
        Assert.Single(world.Prefabs);
        Assert.Equal("cabin", world.Prefabs[0].Name);
    }

    [Fact]
    public void Undo_restores_a_moved_prefab_to_where_it_was()
    {
        var world = Flat();
        var info = Poi("cabin", 10, 10);
        var library = PrefabLibrary.Of([info]);
        var prefab = new PrefabInstance { Name = "cabin", Position = new Int3(0, 39, 0) };
        world.Prefabs.Add(prefab);
        var undo = new UndoStack();
        var session = new EditSession("Move");
        PrefabPlacer.Move(world, library, prefab, new PrefabSpot(100, 39, 50, 1), flatten: false, session);
        undo.Push(session);
        Assert.Equal(new Int3(100, 39, 50), world.Prefabs[0].Position);

        undo.Undo();
        Assert.Equal(new Int3(0, 39, 0), world.Prefabs[0].Position);
        Assert.Equal(0, world.Prefabs[0].Rotation);
    }

    [Fact]
    public void Poi_snaps_into_a_street_tile_lot_like_rwg()
    {
        var world = Flat();
        var tile = Tile(lotRotations: 0);
        var poi = Poi("house", 20, 16, yOffset: -3, faceNorth: 2);
        var library = PrefabLibrary.Of([tile, poi]);
        world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(-150, 28, -150), Rotation = 0 });

        // Lot at world x -140..-100, z -130..-100. Cursor in the lot.
        var spot = PrefabPlacer.SpotAt(world, library, poi, -120 + 512, -115 + 512, 0, out var lot);
        Assert.NotNull(lot);
        Assert.True(lot.Fits);
        // Rotation = tile 0 + face north 2 + lot 0; centred in x, pushed to the lot's north side.
        Assert.Equal(new PrefabSpot(-140 + 20 - 10, 28 + 12 - 3, -130 + 30 - 16, 2), spot);
        Assert.DoesNotContain(PrefabPlacer.Check(world, library, poi, spot, snappedToLot: true), p => p.StartsWith("overlaps"));
    }

    [Fact]
    public void Too_big_poi_does_not_snap_into_a_lot()
    {
        var world = Flat();
        var tile = Tile();
        var poi = Poi("factory", 60, 60);
        var library = PrefabLibrary.Of([tile, poi]);
        world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(-150, 28, -150) });

        PrefabPlacer.SpotAt(world, library, poi, -120 + 512, -115 + 512, 0, out var lot);
        Assert.NotNull(lot);
        Assert.False(lot.Fits);
    }

    [Fact]
    public void Street_tiles_snap_to_the_grid_and_match_a_neighbours_height()
    {
        var world = Flat(metres: 40);
        var tile = Tile();
        var library = PrefabLibrary.Of([tile]);
        world.Prefabs.Add(new PrefabInstance { Name = tile.Name, Position = new Int3(150 - 512, 55 + 1 - 12, 0 - 512) });

        // Cursor in the next tile east: x 300..450, z 0..150 in grid coords.
        var spot = PrefabPlacer.SpotAt(world, library, tile, 320, 70, 0, out _);
        Assert.Equal(new PrefabSpot(300 - 512, 55 + 1 - 12, -512, 0), spot);

        // With no neighbour it sits on the ground.
        var alone = PrefabPlacer.SpotAt(world, library, tile, 820, 820, 0, out _);
        Assert.Equal(new PrefabSpot(750 - 512, 40 + 1 - 12, 750 - 512, 0), alone);
    }

    [Fact]
    public void Moving_a_street_tile_carries_and_turns_what_is_on_it()
    {
        var world = Flat();
        var tile = Tile();
        var poi = Poi("house", 20, 10);
        var library = PrefabLibrary.Of([tile, poi]);
        var t = new PrefabInstance { Name = tile.Name, Position = new Int3(0, 28, 0) };
        // A 20 x 10 house in the south-west corner of the tile, 5 m in from each edge.
        var h = new PrefabInstance { Name = "house", Position = new Int3(5, 37, 5) };
        world.Prefabs.AddRange([t, h]);

        PrefabPlacer.Move(world, library, t, new PrefabSpot(150, 30, 0, 1), flatten: false, new EditSession("Move"));

        // A counter-clockwise quarter turn takes the south-west corner to the south-east: x' = 150 - (5 + 10), z' = 5.
        Assert.Equal(new Int3(150 + 135, 39, 5), h.Position);
        Assert.Equal(1, h.Rotation);
        Assert.Equal(new Int3(150, 30, 0), t.Position);
    }

    [Fact]
    public void Deleting_a_street_tile_removes_what_is_on_it()
    {
        var world = Flat();
        var tile = Tile();
        var poi = Poi("house", 20, 10);
        var library = PrefabLibrary.Of([tile, poi]);
        var t = new PrefabInstance { Name = tile.Name, Position = new Int3(0, 28, 0) };
        world.Prefabs.AddRange([t, new PrefabInstance { Name = "house", Position = new Int3(5, 37, 5) },
            new PrefabInstance { Name = "house", Position = new Int3(200, 37, 5) }]);

        Assert.Equal(2, PrefabPlacer.Delete(world, library, t, new EditSession("Delete")));
        Assert.Single(world.Prefabs);
    }

    [Fact]
    public void Check_warns_about_overlap_water_and_slope()
    {
        var world = Flat();
        world.Water = new Grid<byte>(world.Size, world.Size);
        var info = Poi("cabin", 20, 20);
        var library = PrefabLibrary.Of([info]);
        world.Prefabs.Add(new PrefabInstance { Name = "cabin", Position = new Int3(0, 39, 0) });

        Assert.Contains("overlaps cabin", PrefabPlacer.Check(world, library, info, new PrefabSpot(10, 39, 10, 0)));
        Assert.Empty(PrefabPlacer.Check(world, library, info, new PrefabSpot(30, 39, 0, 0)));

        world.Water[512 + 100, 512 + 100] = 45;
        Assert.Contains(PrefabPlacer.Check(world, library, info, new PrefabSpot(95, 39, 95, 0)), p => p.EndsWith("under water"));

        world.Height[512 - 100, 512 - 100] = World.MetresToHeight(60);
        Assert.Contains("ground varies by 20 m", PrefabPlacer.Check(world, library, info, new PrefabSpot(-105, 39, -105, 0)));
        Assert.Contains("outside the world", PrefabPlacer.Check(world, library, info, new PrefabSpot(510, 39, 0, 0)));
    }

    [Fact]
    public void Street_tile_parts_land_on_their_markers()
    {
        var tile = Tile();
        var part = new PrefabInfo("part_light", "Parts", new Int3(1, 4, 1), 0, 0, 0, ["part"], null);
        var library = PrefabLibrary.Of([tile, part]);
        var parts = PrefabPlacer.MarkerParts(library, tile, new PrefabSpot(-150, 28, -150, 0), seed: 1);
        var light = Assert.Single(parts);
        Assert.Equal(new Int3(-150 + 70, 28 + 12, -150 + 70), light.Position);
    }

    [Fact]
    public void Spawn_points_stand_a_metre_above_the_ground_and_undo()
    {
        var world = Flat(metres: 40.4f);
        var undo = new UndoStack();
        var session = new EditSession("Add spawn");
        var spawn = SpawnHelper.Add(world, 600, 700, 90, session);
        undo.Push(session);
        Assert.Equal(new[] { 88f, 42f, 188f }, new[] { spawn.X, spawn.Y, spawn.Z });
        Assert.Same(spawn, SpawnHelper.Near(world, 603, 698, 8));
        Assert.Null(SpawnHelper.Near(world, 650, 700, 8));

        undo.Undo();
        Assert.Empty(world.Spawns);
    }

    [Fact]
    public void Replacing_keeps_the_centre_facing_and_ground()
    {
        var world = Flat();
        var cabin = Poi("cabin", 20, 10, yOffset: -2, faceNorth: 0);
        var barn = Poi("barn", 30, 16, yOffset: -4, faceNorth: 1);
        var library = PrefabLibrary.Of([cabin, barn]);
        // A cabin facing west (rotation 1, so 10 x 20) with its centre at world (5, 10), on ground 40.
        var old = new PrefabInstance { Name = "cabin", Position = new Int3(0, 39, 0), Rotation = 1 };
        world.Prefabs.AddRange([new PrefabInstance { Name = "barn", Position = new Int3(300, 37, 300) }, old]);

        var (placed, lot) = PrefabPlacer.Replace(world, library, old, barn, flatten: false, new EditSession("Replace"));

        Assert.Null(lot);
        Assert.DoesNotContain(old, world.Prefabs);
        Assert.Same(placed, world.Prefabs[1]); // same line in prefabs.xml
        Assert.Equal(2, placed.Rotation);      // still facing west: barn faces north at 1
        Assert.Equal(barn.FrontDirection(placed.Rotation), cabin.FrontDirection(old.Rotation));
        // 30 x 16 centred on (5, 10), on the same ground: 40 + 1 - 4.
        Assert.Equal(new Int3(-10, 37, 2), placed.Position);
    }

    [Fact]
    public void Moving_a_poi_carries_its_parts_and_replacing_it_drops_them()
    {
        var world = Flat();
        var station = Poi("gas_station", 40, 40);
        var car = new PrefabInfo("part_car", "Parts", new Int3(3, 2, 5), 0, 0, 0, ["part"], null);
        var library = PrefabLibrary.Of([station, car, Poi("shop", 20, 20)]);
        var s = new PrefabInstance { Name = "gas_station", Position = new Int3(0, 40, 0) };
        var c = new PrefabInstance { Name = "part_car", Position = new Int3(10, 41, 10) };
        world.Prefabs.AddRange([s, c]);

        PrefabPlacer.Move(world, library, s, new PrefabSpot(100, 42, 0, 0), flatten: false, new EditSession("Move"));
        Assert.Equal(new Int3(110, 43, 10), c.Position);

        PrefabPlacer.Replace(world, library, s, library.Find("shop")!, flatten: false, new EditSession("Replace"));
        Assert.Equal(["shop"], world.Prefabs.Select(p => p.Name));
    }
}
