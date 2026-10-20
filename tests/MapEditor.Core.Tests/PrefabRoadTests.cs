using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

/// <summary>Placing prefabs leaves drawn roads as they are; a prefab that meets a road takes its height instead.</summary>
public sealed class PrefabRoadTests
{
    private static float H(World world, int x, int z) => World.HeightToMetres(world.Height[x, z]);

    /// <summary>A flat 40 m world with an east-west asphalt road along z = 300, raised to 43 m.</summary>
    private static World WorldWithRoad()
    {
        var world = new World { Name = "T", Size = 600, Height = new Grid<ushort>(600, 600) };
        Array.Fill(world.Height.Data, World.MetresToHeight(40));
        world.Roads = new Grid<byte>(600, 600);
        for (int z = 295; z < 305; z++)
            for (int x = 0; x < 600; x++)
            {
                world.Roads[x, z] = (byte)RoadType.Asphalt;
                world.Height[x, z] = World.MetresToHeight(43);
            }
        return world;
    }

    [Fact]
    public void Flattening_beside_a_road_leaves_the_road_as_it_was()
    {
        var world = WorldWithRoad();
        PrefabPlacer.Flatten(world, new GridRect(200, 308, 240, 348), 46, PrefabPlacer.StreetTileBlend, new EditSession("Place"));

        for (int x = 150; x < 300; x += 5)
            for (int z = 295; z < 305; z++)
                Assert.Equal(43, H(world, x, z), 2);
        // Away from the road the blend still runs.
        Assert.InRange(H(world, 220, 360), 44, 46);
        // Beside the road it eases in: little next to the road's edge, more a few metres out.
        Assert.InRange(H(world, 220, 294), 40, 40.5f);
        Assert.InRange(H(world, 220, 280), 41.5f, 46);
    }

    [Fact]
    public void A_poi_that_meets_a_road_takes_its_height()
    {
        var world = WorldWithRoad();
        var poi = new PrefabInfo("trader_joel", "POIs", new Int3(40, 20, 40), -2, 0, 0, ["trader"], null);
        var library = PrefabLibrary.Of([poi]);

        // Its south edge (z 306) is 2 m from the road's north edge (z 304).
        var touching = PrefabPlacer.SpotAt(world, library, poi, 220, 326, 0, out _);
        Assert.Equal(PrefabPlacer.PrefabY(poi, 43), touching.Y);
        PrefabPlacer.Place(world, poi, touching, flatten: true, new EditSession("Place"));
        Assert.Equal(43, H(world, 220, 326), 2);
        Assert.Equal(43, H(world, 220, 300), 2);

        // Well away from the road it sits on the ground.
        var away = PrefabPlacer.SpotAt(world, library, poi, 220, 450, 0, out _);
        Assert.Equal(PrefabPlacer.PrefabY(poi, 40), away.Y);
    }
}
