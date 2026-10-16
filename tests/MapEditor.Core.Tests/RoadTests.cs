using System.Numerics;
using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class RoadTests
{
    private static World Flat(int size = 512, float metres = 40)
    {
        var world = new World { Name = "R", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        return world;
    }

    private static float H(World w, int x, int z) => World.HeightToMetres(w.Height[x, z]);

    private static RoadType Road(World w, int x, int z) => (RoadType)w.Roads![x, z];

    [Fact]
    public void Gravel_road_has_the_requested_width()
    {
        var world = Flat();
        var road = new RoadPath([new(100, 200), new(400, 200)], RoadType.Gravel, 4.5f, Grade: false);
        RoadOp.Draw(world, road, new EditSession("Road"));

        // Pixel centres within 4.5 m of z = 200 are road: rows 196..203 (centres 196.5..203.5).
        Assert.Equal(RoadType.Gravel, Road(world, 250, 196));
        Assert.Equal(RoadType.Gravel, Road(world, 250, 203));
        Assert.Equal(RoadType.None, Road(world, 250, 195));
        Assert.Equal(RoadType.None, Road(world, 250, 205));
        Assert.Equal(RoadType.None, Road(world, 50, 200));
        Assert.Equal(40, H(world, 250, 200), 2); // not graded
    }

    [Fact]
    public void Wide_asphalt_gets_a_gravel_shoulder()
    {
        var world = Flat();
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Asphalt, 9), new EditSession("Road"));
        Assert.Equal(RoadType.Asphalt, Road(world, 250, 200));
        Assert.Equal(RoadType.Asphalt, Road(world, 250, 207)); // 7.5 m out, inside the 8 m core
        Assert.Equal(RoadType.Gravel, Road(world, 250, 208));  // 8.5 m out, the shoulder
        Assert.Equal(RoadType.None, Road(world, 250, 210));
    }

    [Fact]
    public void Grading_levels_a_bump_under_the_road_and_blends_at_the_edge()
    {
        var world = Flat();
        // A 2 m high ridge running north-south, crossed by the road.
        for (int z = 0; z < 512; z++)
            for (int x = 245; x < 255; x++) world.Height[x, z] = World.MetresToHeight(42);

        var session = new EditSession("Road");
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Gravel, 4.5f), session);

        // On the road the ridge is shaved well down, since the profile is smoothed along the road.
        Assert.InRange(H(world, 250, 200), 40, 41.2f);
        // Far from the road it is untouched, and just outside the blend it is unchanged.
        Assert.Equal(42, H(world, 250, 300), 2);
        Assert.Equal(42, H(world, 250, 200 + 11), 2);
        // Within the blend it lies between road and ridge.
        float mid = H(world, 250, 200 + 7);
        Assert.InRange(mid, H(world, 250, 200) + 0.01f, 41.99f);

        var stack = new UndoStack();
        stack.Push(session);
        stack.Undo();
        Assert.Equal(42, H(world, 250, 200), 2);
        Assert.Equal(RoadType.None, Road(world, 250, 200));
    }

    [Fact]
    public void Road_over_water_stays_above_the_surface()
    {
        var world = Flat(512, 25);
        world.Water = new Grid<byte>(512, 512);
        Array.Fill(world.Water.Data, (byte)30);
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Asphalt, 4.5f), new EditSession("Road"));
        Assert.True(H(world, 250, 200) >= 31f - 0.01f);
    }

    [Fact]
    public void Road_across_a_deep_lake_does_not_trench_its_approaches()
    {
        // Banks at 45 m, a lake 100 m across with its bed at 20 m and its surface at 38 m.
        var world = Flat(512, 45);
        world.Water = new Grid<byte>(512, 512);
        for (int z = 0; z < 512; z++)
            for (int x = 200; x < 300; x++)
            {
                world.Height[x, z] = World.MetresToHeight(20);
                world.Water[x, z] = 38;
            }

        RoadOp.Draw(world, new RoadPath([new(50, 200), new(450, 200)], RoadType.Asphalt, 9), new EditSession("Road"));

        // The causeway carries the road across at bank height, so the approaches are not cut down at all.
        for (int x = 60; x < 440; x++)
            Assert.True(H(world, x, 200) >= 45 - 0.05f, $"road dips to {H(world, x, 200):0.0} m at x={x}");
        // Beside the causeway the graded bank slopes back down into the lake.
        Assert.InRange(H(world, 250, 200 + 15), 20, 44);
        Assert.Equal(20, H(world, 250, 260), 2);
    }

    [Fact]
    public void Gravel_does_not_overwrite_asphalt_and_erase_clears_roads()
    {
        var world = Flat();
        RoadOp.Draw(world, new RoadPath([new(100, 200), new(400, 200)], RoadType.Asphalt, 4.5f, Grade: false), new EditSession("a"));
        RoadOp.Draw(world, new RoadPath([new(250, 50), new(250, 400)], RoadType.Gravel, 4.5f, Grade: false), new EditSession("g"));
        Assert.Equal(RoadType.Asphalt, Road(world, 250, 200));
        Assert.Equal(RoadType.Gravel, Road(world, 250, 300));

        RoadOp.Erase(world, 250, 200, new BrushTip(20), new EditSession("e"));
        Assert.Equal(RoadType.None, Road(world, 250, 200));
        Assert.Equal(RoadType.Gravel, Road(world, 250, 300));
    }

    [Fact]
    public void Centreline_keeps_end_points_and_rounds_corners()
    {
        var line = RoadOp.Centreline([new(0, 0), new(100, 0), new(100, 100)]);
        Assert.Equal(new Vector2(0, 0), line[0]);
        Assert.Equal(new Vector2(100, 100), line[^1]);
        // The corner at (100, 0) is cut, so no point sits exactly on it.
        Assert.DoesNotContain(line, p => Vector2.Distance(p, new Vector2(100, 0)) < 1);
        Assert.InRange(RoadOp.Length(line), 180, 200);
    }
}
