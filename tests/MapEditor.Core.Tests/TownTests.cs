using System.Numerics;
using System.Xml.Linq;
using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class TownTests
{
    private static readonly RwgMixer Mixer = RwgMixer.Parse(XDocument.Parse("""
        <rwgmixer>
          <world>
            <property name="world_size" value="0,99999"/>
            <property class="town"><property name="tiles" value="9,40"/></property>
            <property class="roadside"><property name="tiles" value="1,1"/></property>
          </world>
          <township name="wilderness"/>
          <township name="town">
            <property name="biomes" value="desert"/>
            <property name="outskirt_district" value="rural, .5"/>
          </township>
          <township name="roadside"><property name="spawn_trader" value="false"/></township>
          <district name="downtown">
            <property name="spawn_weight" value="1"/>
            <property name="required_township" value="town"/>
            <property name="avoided_neighbor_districts" value="rural"/>
          </district>
          <district name="residential">
            <property name="spawn_weight" value="1"/>
            <property name="required_township" value="town"/>
          </district>
          <district name="rural">
            <property name="spawn_weight" value="0"/>
            <property name="required_township" value="town"/>
            <property name="spawn_custom_size_prefabs" value="true"/>
          </district>
          <district name="gateway"><property name="spawn_weight" value="0"/></district>
        </rwgmixer>
        """), 2048);

    private static readonly BiomeTable Biomes = BiomeTable.Vanilla;

    private static TownshipType Town => Mixer.Townships.First(t => t.Name == "town");

    /// <summary>Street tiles of every shape for each district (no gateway caps, as in the game), and a POI per district.</summary>
    private static readonly PrefabLibrary Library = PrefabLibrary.Of(MakeLibrary());

    private static IEnumerable<PrefabInfo> MakeLibrary(string tileFolder = "RWGTiles")
    {
        foreach (var district in new[] { "downtown", "residential", "rural", "gateway" })
            foreach (var shape in TownGenerator.ShapeNames)
            {
                if (district == "gateway" && shape == "cap") continue;
                yield return new PrefabInfo($"rwg_tile_{district}_{shape}", tileFolder, new Int3(150, 22, 150), -12, 0, 0, ["streettile"], null)
                {
                    Markers =
                    [
                        new PrefabMarker(new Int3(10, 12, 10), new Int3(25, 0, 25), MarkerType.POISpawn, "1"),
                        new PrefabMarker(new Int3(90, 12, 90), new Int3(25, 0, 25), MarkerType.POISpawn, "2"),
                        new PrefabMarker(new Int3(70, 12, 70), new Int3(1, 1, 1), MarkerType.PartSpawn, "light", Part: "part_light"),
                    ],
                };
            }
        yield return new PrefabInfo("shop_01", "POIs", new Int3(24, 20, 25), -1, 0, 2, ["downtown"], null);
        yield return new PrefabInfo("house_01", "POIs", new Int3(20, 15, 22), -1, 0, 1, ["residential"], null);
        yield return new PrefabInfo("house_02", "POIs", new Int3(25, 15, 25), -1, 0, 1, ["residential"], null);
        yield return new PrefabInfo("farm_01", "POIs", new Int3(18, 10, 20), -1, 0, 1, ["rural"], null);
        yield return new PrefabInfo("part_light", "Parts", new Int3(1, 5, 1), 0, 0, 0, ["part"], null);
    }

    private static World Flat(int size = 1500, float metres = 40)
    {
        var world = new World { Name = "T", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        return world;
    }

    private static TownTile? At(TownPlan plan, int cx, int cz) => plan.Tiles.FirstOrDefault(t => t.Cx == cx && t.Cz == cz);

    /// <summary>The tile covering grid point (x, z).</summary>
    private static TownTile? Covering(TownPlan plan, int x, int z) =>
        plan.Tiles.FirstOrDefault(t => x >= t.X0 && x < t.X0 + 150 && z >= t.Z0 && z < t.Z0 + 150);

    private static readonly (int X, int Z)[] Dirs = [(0, 1), (1, 0), (0, -1), (-1, 0)];

    [Fact]
    public void Road_shapes_turn_clockwise_and_cover_every_exit_set()
    {
        Assert.Equal((0, 0), TownGenerator.ShapeFor(0b0101)); // straight north-south
        Assert.Equal((0, 1), TownGenerator.ShapeFor(0b1010)); // straight east-west
        Assert.Equal((2, 0), TownGenerator.ShapeFor(0b1111));
        Assert.Equal((3, 0), TownGenerator.ShapeFor(0b0100)); // the cap's street leaves south
        Assert.Equal((3, 2), TownGenerator.ShapeFor(0b0001));
        for (int exits = 1; exits < 16; exits++) Assert.NotEqual(-1, TownGenerator.ShapeFor(exits).Shape);
        Assert.Equal(-1, TownGenerator.ShapeFor(0).Shape);
    }

    [Fact]
    public void Tile_rotation_swaps_one_and_three_like_spawnStreetTile()
    {
        var tile = Library.Find("rwg_tile_residential_cap")!;
        Assert.Equal(0, TownGenerator.TileRotation(tile, 0));
        Assert.Equal(3, TownGenerator.TileRotation(tile, 1));
        Assert.Equal(2, TownGenerator.TileRotation(tile, 2));
        Assert.Equal(1, TownGenerator.TileRotation(tile, 3));
    }

    [Fact]
    public void Districts_take_their_share_from_the_centre_out()
    {
        var tiles = new List<TownTile>();
        for (int z = 0; z < 3; z++)
            for (int x = 0; x < 3; x++)
                tiles.Add(new TownTile(10 + x, 10 + z));
        var districts = new[] { Mixer.Districts["residential"], Mixer.Districts["downtown"] };

        var left = TownGenerator.AssignDistricts(tiles, districts, TownGenerator.CentreTile(tiles));

        Assert.Empty(left);
        // Downtown goes first and takes round(9 / 2) = 4 (to even), starting at the centre; residential takes the rest.
        Assert.Equal("downtown", tiles.Single(t => (t.Cx, t.Cz) == (11, 11)).District!.Name);
        Assert.Equal(4, tiles.Count(t => t.District!.Name == "downtown"));
        Assert.Equal(5, tiles.Count(t => t.District!.Name == "residential"));
    }

    [Fact]
    public void Downtown_keeps_away_from_rural_outskirts()
    {
        // A row of four with rural at the west end: downtown may not start next to it.
        var rural = new TownTile(0, 0) { District = Mixer.Districts["rural"] };
        var tiles = new List<TownTile> { rural, new(1, 0), new(2, 0), new(3, 0) };
        TownGenerator.AssignDistricts(tiles, [Mixer.Districts["downtown"], Mixer.Districts["residential"]], tiles[1]);
        Assert.NotEqual("downtown", tiles[1].District?.Name);
    }

    [Fact]
    public void A_planned_town_has_a_tile_on_every_cell_with_matching_streets()
    {
        var world = Flat();
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 7));

        Assert.NotEmpty(plan.Tiles);
        Assert.All(plan.Tiles, t => Assert.NotNull(t.Prefab));
        Assert.All(plan.Tiles, t => Assert.NotEqual(0, t.Exits));
        Assert.Contains(plan.Tiles, t => t.District!.Name == "downtown");
        Assert.Contains(plan.Tiles, t => t.District!.Name == "rural");
        Assert.Contains(plan.Tiles, t => t.IsGateway);

        foreach (var t in plan.Tiles)
            for (int dir = 0; dir < 4; dir++)
            {
                // A street into a neighbour is met by one coming back.
                if (At(plan, t.Cx + Dirs[dir].X, t.Cz + Dirs[dir].Z) is not { } n) continue;
                Assert.Equal((t.Exits >> dir) & 1, (n.Exits >> ((dir + 2) & 3)) & 1);
            }

        foreach (var g in plan.Tiles.Where(t => t.IsGateway))
        {
            Assert.Equal(1, Dirs.Count(d => At(plan, g.Cx + d.X, g.Cz + d.Z) is not null));
            Assert.StartsWith("rwg_tile_gateway_straight", g.Prefab!.Name); // into town and out the far side
        }

        // Every tile's prefab matches its streets.
        foreach (var t in plan.Tiles)
            Assert.StartsWith($"rwg_tile_{t.District!.PrefabName}{TownGenerator.ShapeNames[TownGenerator.ShapeFor(t.Exits).Shape]}", t.Prefab!.Name);

        // Ground: ceil(40) + 3, tiles at ground + 1 + YOffset.
        Assert.Equal(43, plan.Ground);
        Assert.All(plan.Tiles, t => Assert.Equal(43 + 1 - 12, t.Prefab!.Position.Y));
        Assert.True(plan.PoiCount > 0);
        Assert.True(plan.PartCount > 0);
    }

    [Fact]
    public void POIs_go_into_lots_of_their_district()
    {
        var world = Flat();
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 3));
        foreach (var poi in plan.Prefabs.Where(p => !p.Name.StartsWith("rwg_tile_") && p.Name != "part_light"))
        {
            int gx = world.WorldToGrid(poi.Position.X), gz = world.WorldToGrid(poi.Position.Z);
            var tile = Covering(plan, gx, gz)!;
            Assert.Contains(tile.District!.Name, Library.Find(poi.Name)!.Tags);
            // On the tile's level: tile y + marker y + the POI's YOffset.
            Assert.Equal(tile.Prefab!.Position.Y + 12 - 1, poi.Position.Y);
        }
    }

    [Fact]
    public void The_same_seed_gives_the_same_town()
    {
        var world = Flat();
        var sel = new GridRect(150, 150, 1200, 1200);
        string Key(TownPlan p) => string.Join(";", p.Prefabs.Select(i => $"{i.Name}@{i.Position}/{i.Rotation}"));
        var a = TownGenerator.Plan(world, Library, Mixer, Biomes, sel, new TownOptions(Town, 11));
        var b = TownGenerator.Plan(world, Library, Mixer, Biomes, sel, new TownOptions(Town, 11));
        Assert.Equal(Key(a), Key(b));
    }

    [Fact]
    public void Applying_flattens_the_town_and_undo_puts_everything_back()
    {
        var world = Flat();
        // A hill in the middle of the town.
        for (int z = 600; z < 700; z++)
            for (int x = 600; x < 700; x++)
                world.Height[x, z] = World.MetresToHeight(55);
        var before = (ushort[])world.Height.Data.Clone();
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 5));

        var session = new EditSession("Build town");
        TownGenerator.Apply(world, Library, plan, session);
        foreach (var t in plan.Tiles)
            Assert.Equal(plan.Ground, World.HeightToMetres(world.Height[t.X0 + 75, t.Z0 + 75]), 0.01);
        Assert.Equal(plan.Prefabs.Count, world.Prefabs.Count);

        var undo = new UndoStack();
        undo.Push(session);
        undo.Undo();
        Assert.Equal(before, world.Height.Data);
        Assert.Empty(world.Prefabs);
    }

    [Fact]
    public void Prefabs_in_the_way_are_replaced_or_left_alone()
    {
        var world = Flat();
        var house = new PrefabInstance { Name = "house_01", Position = new Int3(world.GridToWorld(620), 41, world.GridToWorld(620)) };
        world.Prefabs.Add(house);
        var sel = new GridRect(150, 150, 1200, 1200);

        var replace = TownGenerator.Plan(world, Library, Mixer, Biomes, sel, new TownOptions(Town, 1));
        Assert.Contains(house, replace.Removed);

        var keep = TownGenerator.Plan(world, Library, Mixer, Biomes, sel, new TownOptions(Town, 1) { ReplacePrefabs = false });
        Assert.Empty(keep.Removed);
        Assert.Null(Covering(keep, 620, 620));
    }

    [Fact]
    public void Cells_start_at_the_selection_corner()
    {
        var world = Flat();
        // 3 x 2 cells from (100, 70), well off the world's 150 m grid.
        var sel = new GridRect(100, 70, 100 + 450 + 40, 70 + 300 + 20);
        Assert.Equal(6, TownGenerator.CellsIn(world, sel).Count);

        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, sel, new TownOptions(Town, 1));
        Assert.NotEmpty(plan.Tiles);
        Assert.All(plan.Tiles, t =>
        {
            Assert.Equal(0, (t.X0 - 100) % 150);
            Assert.Equal(0, (t.Z0 - 70) % 150);
        });
        Assert.Contains(plan.Tiles, t => t.X0 == 100);
        Assert.Contains(plan.Tiles, t => t.Z0 + 150 == 370); // the town reaches the selection's north edge
    }

    [Fact]
    public void A_street_tile_placed_by_hand_follows_the_grid_of_the_town_beside_it()
    {
        var world = Flat();
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(100, 70, 550, 370), new TownOptions(Town, 1));
        TownGenerator.Apply(world, Library, plan, new EditSession("Town"));
        var tile = Library.Find("rwg_tile_residential_straight")!;
        // Just east of the town: the tile lines up with it, not with the world's grid.
        var spot = PrefabPlacer.SpotAt(world, Library, tile, 640, 140, 0, out _);
        Assert.Equal(550, world.WorldToGrid(spot.X));
        Assert.Equal(70, world.WorldToGrid(spot.Z));
    }

    [Fact]
    public void Water_cells_are_left_out()
    {
        var world = Flat();
        world.Water = new Grid<byte>(world.Size, world.Size);
        for (int z = 450; z < 600; z++)
            for (int x = 450; x < 600; x++)
                world.Water[x, z] = 45;
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 1));
        Assert.Null(Covering(plan, 500, 500));
        Assert.Contains(plan.Warnings, w => w.Contains("water"));
    }

    [Fact]
    public void Exits_join_the_nearest_road_ahead()
    {
        var world = Flat();
        world.Roads = new Grid<byte>(world.Size, world.Size);
        // An east-west highway 100 m south of the selection.
        for (int z = 48; z < 52; z++)
            for (int x = 0; x < world.Size; x++)
                world.Roads[x, z] = (byte)RoadType.Asphalt;
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 2));

        var south = plan.Tiles.Where(t => t.IsGateway && (t.Exits & 0b0100) != 0 && At(plan, t.Cx, t.Cz - 1) is null).ToList();
        Assert.NotEmpty(south);
        Assert.Contains(plan.Roads, r => r.Points[^1].Y < 52 && r.Type == RoadType.Asphalt);

        TownGenerator.Apply(world, Library, plan, new EditSession("Build town"));
        var exit = south[0].ExitPoint(2);
        Assert.Equal((byte)RoadType.Asphalt, world.Roads[(int)exit.X, (int)exit.Y - 10]);
    }

    [Fact]
    public void Link_roads_copy_the_surface_and_width_of_the_road_they_join()
    {
        var world = Flat();
        // A narrow gravel road 100 m south of a town that would otherwise get 9 m asphalt.
        RoadOp.Draw(world, new RoadPath([new(0, 50), new(world.Size, 50)], RoadType.Gravel, 3f, Grade: false), new EditSession("Road"));
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 1200), new TownOptions(Town, 2));

        Assert.NotEmpty(plan.Roads);
        Assert.All(plan.Roads, r =>
        {
            Assert.Equal(RoadType.Gravel, r.Type);
            Assert.Equal(3f, r.Radius);
        });
    }

    /// <summary>A library with one trader, tagged so a district would pick it if traders were left to chance.</summary>
    private static PrefabLibrary WithTrader(int size, string tag = "residential") => PrefabLibrary.Of(MakeLibrary()
        .Append(new PrefabInfo("trader_test", "POIs", new Int3(size, 20, size), -1, 0, 1, [tag], null)).ToList());

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public void A_town_gets_the_number_of_traders_asked_for(int traders)
    {
        var plan = TownGenerator.Plan(Flat(), WithTrader(20), Mixer, Biomes, new GridRect(150, 150, 1200, 1200),
            new TownOptions(Town, 3) { Traders = traders });
        Assert.Equal(traders, plan.TraderCount);
        Assert.Equal(traders, plan.Prefabs.Count(p => p.Name == "trader_test"));
    }

    [Fact]
    public void A_small_town_gets_its_trader_near_the_middle()
    {
        // A trader no district asks for, which RWG's picks would never place.
        var plan = TownGenerator.Plan(Flat(), WithTrader(24, "trader"), Mixer, Biomes, new GridRect(150, 150, 450, 450),
            new TownOptions(Town, 3));
        var trader = Assert.Single(plan.Prefabs, p => p.Name == "trader_test");
        var bounds = plan.Bounds;
        var world = Flat();
        float dx = world.WorldToGrid(trader.Position.X) + 12 - (bounds.X0 + bounds.X1) / 2f;
        float dz = world.WorldToGrid(trader.Position.Z) + 12 - (bounds.Z0 + bounds.Z1) / 2f;
        Assert.True(MathF.Sqrt(dx * dx + dz * dz) < 150, $"trader {dx}, {dz} from the middle");
    }

    [Fact]
    public void Traders_are_spread_apart()
    {
        var plan = TownGenerator.Plan(Flat(), WithTrader(20), Mixer, Biomes, new GridRect(150, 150, 1200, 1200),
            new TownOptions(Town, 3) { Traders = 3 });
        var spots = plan.Prefabs.Where(p => p.Name == "trader_test").Select(p => new Vector2(p.Position.X, p.Position.Z)).ToList();
        Assert.Equal(3, spots.Count);
        for (int i = 0; i < spots.Count; i++)
            for (int j = i + 1; j < spots.Count; j++)
                Assert.True(Vector2.Distance(spots[i], spots[j]) > 150, $"traders {i} and {j} are {Vector2.Distance(spots[i], spots[j]):0} m apart");
    }

    [Fact]
    public void A_warning_says_when_no_lot_is_big_enough_for_a_trader()
    {
        var plan = TownGenerator.Plan(Flat(), WithTrader(60), Mixer, Biomes, new GridRect(150, 150, 450, 450),
            new TownOptions(Town, 3) { Traders = 2 });
        Assert.Equal(0, plan.TraderCount);
        Assert.Contains(plan.Warnings, w => w.Contains("no lot in this town is big enough"));
    }

    [Fact]
    public void Link_roads_can_match_the_street_they_leave_from()
    {
        // Every tile's blocks are a 10 m asphalt cross through its middle, so each exit has a 10 m street.
        string dir = Directory.CreateTempSubdirectory("mapeditor-tiles-").FullName;
        try
        {
            var library = PrefabLibrary.Of(MakeLibrary(dir).ToList());
            foreach (var info in library.All.Where(p => p.IsStreetTile))
            {
                using (var w = new BinaryWriter(File.Create(Path.Combine(dir, info.Name + ".tts"))))
                {
                    w.Write("tts\0"u8);
                    w.Write(19u);
                    w.Write((short)150);
                    w.Write((short)1);
                    w.Write((short)150);
                    for (int z = 0; z < 150; z++)
                        for (int x = 0; x < 150; x++)
                            w.Write(x is >= 70 and < 80 || z is >= 70 and < 80 ? 2u : 1u);
                }
                using (var w = new BinaryWriter(File.Create(Path.Combine(dir, info.Name + ".blocks.nim")), System.Text.Encoding.UTF8))
                {
                    w.Write(1);
                    w.Write(2);
                    w.Write(1);
                    w.Write("terrDirt");
                    w.Write(2);
                    w.Write("terrAsphalt");
                }
            }

            var world = Flat();
            RoadOp.Draw(world, new RoadPath([new(0, 50), new(world.Size, 50)], RoadType.Gravel, 3f, Grade: false), new EditSession("Road"));
            var selection = new GridRect(150, 150, 1200, 1200);
            var joined = TownGenerator.Plan(world, library, Mixer, Biomes, selection, new TownOptions(Town, 2));
            var street = TownGenerator.Plan(world, library, Mixer, Biomes, selection, new TownOptions(Town, 2) { LinkStyle = LinkRoadStyle.Tile });

            Assert.NotEmpty(street.Roads);
            Assert.All(joined.Roads, r => Assert.Equal((RoadType.Gravel, 3f), (r.Type, r.Radius)));
            Assert.All(street.Roads, r => Assert.Equal((RoadType.Asphalt, 5f), (r.Type, r.Radius)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void A_tile_street_counts_its_shoulder_but_not_gravel_beside_it()
    {
        // A north-south street at the south edge: 14 m asphalt, 1 m gravel shoulders, then a wide gravel verge east.
        var pixels = new uint[150 * 150];
        uint asphalt = 0xFF3C3C40, gravel = 0xFFB49A6A;
        for (int row = 0; row < 150; row++)
            for (int x = 0; x < 150; x++)
                pixels[row * 150 + x] = x switch
                {
                    >= 68 and < 82 => asphalt,
                    67 or 82 => gravel,
                    > 82 and < 110 => gravel,
                    _ => 0,
                };
        var street = TownGenerator.StreetAcross(new PrefabTopView(150, 150, pixels), 2);
        Assert.Equal((RoadType.Asphalt, 8f), street);
    }

    [Theory]
    [InlineData(RoadType.Asphalt, 9f, 0f)]
    [InlineData(RoadType.Asphalt, 9f, 0.6f)]
    [InlineData(RoadType.Gravel, 4.5f, 0f)]
    [InlineData(RoadType.Gravel, 2.5f, 1.1f)]
    public void A_painted_road_is_measured_from_its_pixels(RoadType type, float radius, float angle)
    {
        var world = Flat(600);
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        var centre = new Vector2(300, 300);
        RoadOp.Draw(world, new RoadPath([centre - dir * 250, centre + dir * 250], type, radius, Grade: false), new EditSession("Road"));

        // Measured from a pixel at the road's edge, as the link roads do.
        var across = new Vector2(-dir.Y, dir.X);
        var edge = centre + across * (radius - 0.5f);
        var measured = TownGenerator.MeasureRoad(world.Roads!, new Vector2(MathF.Floor(edge.X), MathF.Floor(edge.Y)));

        Assert.NotNull(measured);
        Assert.Equal(type, measured.Value.Type);
        Assert.InRange(measured.Value.Radius, radius - 0.5f, radius + 0.5f);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Link_roads_keep_out_of_a_neighbouring_town(int seed)
    {
        var world = Flat();
        world.Roads = new Grid<byte>(world.Size, world.Size);
        // An east-west highway south of both towns; the first town sits between it and the second.
        for (int z = 48; z < 52; z++)
            for (int x = 0; x < world.Size; x++)
                world.Roads[x, z] = (byte)RoadType.Asphalt;
        var first = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 150, 1200, 600), new TownOptions(Town, seed));
        TownGenerator.Apply(world, Library, first, new EditSession("First town"));

        var second = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(150, 600, 1200, 1200),
            new TownOptions(Town, seed) { ReplacePrefabs = false });
        Assert.NotEmpty(second.Tiles);
        var firstCells = first.Tiles.Select(t => t.Cells).ToList();
        foreach (var road in second.Roads)
            foreach (var p in RoadOp.Centreline(road.Points, RoadOp.CornerRounding(road.Type)))
                Assert.DoesNotContain(firstCells, c => p.X > c.X0 - road.Radius && p.X < c.X1 + road.Radius
                                                       && p.Y > c.Z0 - road.Radius && p.Y < c.Z1 + road.Radius);
    }

    [Fact]
    public void A_roadside_stop_is_one_gateway_running_toward_the_road()
    {
        var world = Flat();
        world.Roads = new Grid<byte>(world.Size, world.Size);
        for (int z = 0; z < world.Size; z++) world.Roads[1000, z] = (byte)RoadType.Gravel; // a road to the east
        var roadside = Mixer.Townships.First(t => t.Name == "roadside");
        var plan = TownGenerator.Plan(world, Library, Mixer, Biomes, new GridRect(600, 600, 750, 750), new TownOptions(roadside, 1));
        var tile = Assert.Single(plan.Tiles);
        Assert.True(tile.IsGateway);
        Assert.Equal(0b1010, tile.Exits);
    }

    [Fact]
    public void The_default_township_suits_the_biome_and_size()
    {
        Assert.Equal("town", TownGenerator.DefaultTownship(Mixer, "desert", 20)!.Name);
        Assert.Equal("roadside", TownGenerator.DefaultTownship(Mixer, "forest", 1)!.Name);
        Assert.Equal("forest", TownGenerator.BiomeTag("pine_forest"));
        Assert.Equal("burntforest", TownGenerator.BiomeTag("burnt_forest"));
    }
}
