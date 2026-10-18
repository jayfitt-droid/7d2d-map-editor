using MapEditor.Core.Edit;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class ResmoothTests(Xunit.Abstractions.ITestOutputHelper output)
{
    /// <summary>
    /// A flat 40 m world with a graded east-west asphalt road along z = 100, and a row of four 150 m town tiles 30 m
    /// north of it (x 150 to 750), each flattened to 44 m with the town blend in turn, as a town build does.
    /// </summary>
    private static (World World, List<GridRect> Tiles, GridRect Area) TownBesideRoad(EditSession session)
    {
        var world = new World { Name = "T", Size = 900, Height = new Grid<ushort>(900, 900) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        RoadOp.Draw(world, new RoadPath([new(0, 100), new(900, 100)], RoadType.Asphalt, 8f), new EditSession("Road"));
        var tiles = Enumerable.Range(0, 4).Select(i => new GridRect(150 + i * 150, 130, 300 + i * 150, 280)).ToList();
        var area = GridRect.Empty;
        foreach (var t in tiles) area = area.Union(PrefabPlacer.Flatten(world, t, 44, 110, session));
        return (world, tiles, area);
    }

    private static float H(World world, int x, int z) => World.HeightToMetres(world.Height[x, z]);

    /// <summary>How much the road rises and falls beside the middle of the town, where it should run level.</summary>
    private static float Roll(World world)
    {
        var heights = Enumerable.Range(0, 76).Select(i => H(world, 300 + i * 4, 100)).ToList();
        return heights.Max() - heights.Min();
    }

    [Fact]
    public void A_road_beside_a_town_rolls_less_after_resmoothing()
    {
        var (plain, _, _) = TownBesideRoad(new EditSession("Town"));
        var session = new EditSession("Town");
        var (smoothed, tiles, area) = TownBesideRoad(session);
        RoadOp.Resmooth(smoothed, area, tiles, session);
        output.WriteLine($"roll beside the town: {Roll(plain):0.000} m before, {Roll(smoothed):0.000} m after");

        Assert.True(Roll(plain) > 0.1f, $"the tiles' blends should leave the road rolling ({Roll(plain):0.000} m)");
        Assert.True(Roll(smoothed) < Roll(plain) / 3, $"{Roll(smoothed):0.000} vs {Roll(plain):0.000} m");
    }

    [Fact]
    public void Resmoothing_keeps_the_road_level_across_and_leaves_far_ground_alone()
    {
        var session = new EditSession("Town");
        var (world, tiles, area) = TownBesideRoad(session);
        var changed = RoadOp.Resmooth(world, area, tiles, session);

        // Across the 16 m road the height barely changes.
        for (int x = 100; x < 800; x += 10)
            Assert.InRange(MathF.Abs(H(world, x, 95) - H(world, x, 105)), 0, 0.15f);
        // Nothing beyond the fade moved, and the tiles keep the town's level.
        Assert.True(changed.X0 >= area.X0 - RoadOp.ResmoothReach && changed.X1 <= area.X1 + RoadOp.ResmoothReach);
        Assert.Equal(44f, H(world, 400, 200), 0.05f);
    }

    [Fact]
    public void Ground_without_roads_is_left_alone()
    {
        var world = new World { Name = "T", Size = 400, Height = new Grid<ushort>(400, 400), Roads = new Grid<byte>(400, 400) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        var before = (ushort[])world.Height.Data.Clone();
        Assert.True(RoadOp.Resmooth(world, new GridRect(100, 100, 300, 300), [], new EditSession("Nothing")).IsEmpty);
        Assert.Equal(before, world.Height.Data);
    }
}
