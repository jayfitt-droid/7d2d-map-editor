using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;

namespace MapEditor.Core.Tests;

public sealed class PaintTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-paint-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World Flat(int size = 512, float metres = 40)
    {
        var world = new World { Name = "P", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        world.Biomes = LayerPaint.NewBiomeLayer(size);
        return world;
    }

    [Fact]
    public void Biome_paint_adds_the_colour_and_undo_restores_it()
    {
        var world = Flat();
        var stack = new UndoStack();
        var session = new EditSession("Biome");
        var rect = LayerPaint.PaintBiome(world, 256, 256, 40, 0xFFE477, session);
        stack.Push(session);

        Assert.Contains(0xFFE477u, world.Biomes!.Palette);
        uint Rgb(int x, int z) => world.Biomes.Palette[world.Biomes.Indices[x / 8, z / 8]];
        Assert.Equal(0xFFE477u, Rgb(256, 256));
        Assert.Equal(LayerPaint.DefaultBiome, Rgb(256, 320));
        Assert.Equal(0, rect.X0 % 8);
        Assert.True(rect.X0 <= 216 && rect.X1 >= 296);

        stack.Undo();
        Assert.Equal(LayerPaint.DefaultBiome, Rgb(256, 256));
    }

    [Fact]
    public void Painted_biomes_and_radiation_survive_a_save()
    {
        var world = Flat();
        LayerPaint.PaintBiome(world, 100, 100, 50, 0xBA00FF, new EditSession("b"));
        LayerPaint.PaintRadiation(world, 400, 400, 64, erase: false, new EditSession("r"));
        world.ExtraFiles[WorldFiles.Level] = [.. "ttw\0"u8, 32, .. "00000000000000000000000000000000"u8];

        WorldWriter.Write(world, _dir);
        var loaded = WorldReader.Read(_dir);

        Assert.Empty(WorldComparer.Compare(world, loaded));
        Assert.Equal(LayerPaint.RadiationOn, loaded.Radiation![400 / 32, 400 / 32]);
        Assert.Equal(0, loaded.Radiation[100 / 32, 100 / 32]);
        var info = WorldQuery.At(loaded, 100, 100, BiomeTable.Vanilla, PrefabLibrary.Empty)!;
        Assert.Equal("burnt_forest", info.Biome);
    }

    [Fact]
    public void Radiation_erase_clears_cells()
    {
        var world = Flat();
        LayerPaint.PaintRadiation(world, 256, 256, 100, erase: false, new EditSession("r"));
        LayerPaint.PaintRadiation(world, 256, 256, 100, erase: true, new EditSession("e"));
        Assert.All(world.Radiation!.Data, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Water_fill_follows_the_basin_and_stops_at_the_rim()
    {
        // Two pits 10 m deep separated by a ridge; filling one must not reach the other.
        var world = Flat(256, 40);
        for (int z = 0; z < 256; z++)
            for (int x = 0; x < 256; x++)
                if (z is > 50 and < 200 && (x is > 30 and < 110 || x is > 140 and < 220))
                    world.Height[x, z] = World.MetresToHeight(30);

        var stack = new UndoStack();
        var session = new EditSession("Fill");
        var result = WaterFill.Fill(world, 70, 120, 35, session);
        stack.Push(session);

        Assert.Equal(79L * 149, result.Cells);
        Assert.Equal(35, world.Water![70, 120]);
        Assert.Equal(35, world.Water[30, 120]); // shoreline cell just outside the basin
        Assert.Equal(0, world.Water[29, 120]);
        Assert.Equal(0, world.Water[180, 120]); // the other pit stays dry

        // Filling from dry, high ground does nothing.
        Assert.Equal(0, WaterFill.Fill(world, 5, 5, 35, new EditSession("x")).Cells);

        var drain = new EditSession("Drain");
        WaterFill.Drain(world, 70, 120, drain);
        Assert.All(world.Water.Data, v => Assert.Equal(0, v));

        stack.Push(drain);
        stack.Undo();
        Assert.Equal(35, world.Water[70, 120]);
        stack.Undo();
        Assert.All(world.Water.Data, v => Assert.Equal(0, v));
    }

    [Fact]
    public void Drain_leaves_water_with_a_different_surface()
    {
        var world = Flat(128, 40);
        world.Water = new Grid<byte>(128, 128);
        for (int x = 0; x < 128; x++)
        {
            world.Water[x, 0] = 30; // the sea along the south edge
            world.Water[x, 1] = 45; // a higher lake touching it
        }
        WaterFill.Drain(world, 10, 1, new EditSession("d"));
        Assert.Equal(0, world.Water[10, 1]);
        Assert.Equal(30, world.Water[10, 0]);
    }

    [Fact]
    public void Renderer_shows_a_biome_colour_painted_after_it_was_created()
    {
        var world = Flat(256);
        var renderer = new MapRenderer(world, BiomeTable.Vanilla);
        var before = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(0, 0, 0, MapLayers.Biomes, before);

        LayerPaint.PaintBiome(world, 128, 128, 30, 0xFFFFFF, new EditSession("snow"));
        var after = new uint[before.Length];
        renderer.RenderTile(0, 0, 0, MapLayers.Biomes, after);

        // Pixel at the centre (row 127 from the north is z 128).
        Assert.NotEqual(before[128 + 127 * 256], after[128 + 127 * 256]);
        Assert.Equal(before[5], after[5]);
    }
}
