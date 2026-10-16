using MapEditor.Core.GameData;
using MapEditor.Core.Model;
using MapEditor.Core.Rendering;

namespace MapEditor.Core.Tests;

public sealed class MapRendererTests
{
    private static World FlatWorld(int size)
    {
        var world = new World { Name = "R", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(20));
        world.Water = new Grid<byte>(size, size);
        world.Roads = new Grid<byte>(size, size);
        return world;
    }

    [Theory]
    [InlineData(256, 1)]
    [InlineData(512, 2)]
    [InlineData(8192, 6)]
    [InlineData(10240, 7)]
    public void Top_level_fits_the_world_in_one_tile(int size, int levels)
    {
        Assert.Equal(levels, MapRenderer.LevelCount(size));
        var renderer = new MapRenderer(FlatWorld(size), BiomeTable.Vanilla);
        Assert.Equal(1, renderer.TilesAcross(levels - 1));
    }

    [Fact]
    public void Tiles_are_north_up()
    {
        var world = FlatWorld(512);
        world.Water![0, 511] = 30; // north-west corner
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(0, 0, 0, MapLayers.Water, tile);

        Assert.True(IsBlueish(tile[0]));
        Assert.False(IsBlueish(tile[1]));
        Assert.False(IsBlueish(tile[(MapRenderer.TileSize - 1) * MapRenderer.TileSize]));
    }

    [Fact]
    public void Thin_roads_survive_at_coarse_levels()
    {
        var world = FlatWorld(2048);
        for (int z = 0; z < 2048; z++) world.Roads![1000, z] = (byte)RoadType.Asphalt;
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];

        renderer.RenderTile(3, 0, 0, MapLayers.Roads, tile); // 8 m per pixel

        uint road = tile[1000 / 8];
        uint ground = tile[10];
        Assert.NotEqual(ground, road);
    }

    [Fact]
    public void Pixels_outside_the_world_are_transparent()
    {
        var renderer = new MapRenderer(FlatWorld(384), BiomeTable.Vanilla);
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(0, 1, 1, MapLayers.All, tile);
        Assert.NotEqual(0u, tile[0]);
        Assert.Equal(0u, tile[200]);
        Assert.Equal(0u, tile[200 * MapRenderer.TileSize]);
    }

    private static bool IsBlueish(uint bgra) => (bgra & 0xFF) > ((bgra >> 16) & 0xFF) + 40;
}
