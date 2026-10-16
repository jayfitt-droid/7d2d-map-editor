using MapEditor.Core.Edit;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

public sealed class EditingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-edit-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World Flat(int size = 256, float metres = 40)
    {
        var world = new World { Name = "E", Size = size, Height = new Grid<ushort>(size, size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        return world;
    }

    private static float H(World w, int x, int z) => World.HeightToMetres(w.Height[x, z]);

    [Fact]
    public void Raise_lifts_the_centre_most_and_leaves_outside_alone()
    {
        var world = Flat();
        var session = new EditSession("Raise");
        TerrainBrush.Dab(world.Height, 100, 100, new BrushSettings(BrushMode.Raise, 20, 1), session);

        Assert.InRange(H(world, 100, 100), 43.9f, 44.1f);
        Assert.InRange(H(world, 110, 100), 40.1f, 43.9f);
        Assert.Equal(40, H(world, 125, 100), 2);
    }

    [Fact]
    public void Undo_and_redo_restore_exact_heights()
    {
        var world = Flat();
        var before = (ushort[])world.Height.Data.Clone();
        var stack = new UndoStack();
        var session = new EditSession("Lower");
        for (int i = 0; i < 5; i++)
            TerrainBrush.Dab(world.Height, 60 + i * 10, 70, new BrushSettings(BrushMode.Lower, 30, 0.5f), session);
        stack.Push(session);
        var after = (ushort[])world.Height.Data.Clone();

        var step = stack.Undo();
        Assert.NotNull(step);
        Assert.Equal(before, world.Height.Data);
        Assert.False(step.Bounds.IsEmpty);

        stack.Redo();
        Assert.Equal(after, world.Height.Data);
    }

    [Fact]
    public void Flatten_moves_toward_target_and_smooth_reduces_roughness()
    {
        var world = Flat();
        var noisy = new EditSession("n");
        TerrainBrush.Dab(world.Height, 128, 128, new BrushSettings(BrushMode.Noise, 80, 1, NoiseScale: 8), noisy);
        double Roughness() => Enumerable.Range(100, 50).Sum(x => Math.Abs(H(world, x, 128) - H(world, x + 1, 128)));
        double rough = Roughness();

        TerrainBrush.Dab(world.Height, 128, 128, new BrushSettings(BrushMode.Smooth, 80, 1), new EditSession("s"));
        Assert.True(Roughness() < rough * 0.7, "smoothing should reduce roughness");

        TerrainBrush.Dab(world.Height, 128, 128, new BrushSettings(BrushMode.Flatten, 40, 1, TargetHeight: 70), new EditSession("f"));
        Assert.InRange(H(world, 128, 128), 69.9f, 70.1f);
    }

    [Fact]
    public void Raw_stamp_channels_decode_like_the_game()
    {
        // 2x2 stamp: height 51 m everywhere, alpha ramps, water only in one pixel.
        var bytes = new byte[4 * 6];
        for (int i = 0; i < 4; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 6), (ushort)(51 * 65535 / 255));
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 6 + 2), (ushort)(i * 20000));
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 6 + 4), (ushort)(i == 3 ? 65535 : 0));
        }
        string path = Path.Combine(_dir, "test_01.raw");
        File.WriteAllBytes(path, bytes);

        var stamp = StampLibrary.Load(_dir).Get("test_01");
        Assert.Equal(2, stamp.Size);
        Assert.Null(stamp.Height);
        Assert.Equal(51, stamp.HeightConst, 1);
        Assert.NotNull(stamp.Alpha);
        Assert.True(stamp.HasWater);
    }

    [Fact]
    public void Stamp_blends_toward_its_height_and_fills_water_below_the_surface()
    {
        var world = Flat(512, 40);
        var stamp = new StampImage { Name = "pit", Size = 64, HeightConst = 20, AlphaConst = 1, Water = Enumerable.Repeat(255f, 64 * 64).ToArray() };

        StampOp.Apply(world, stamp, new StampPlacement(256, 256, 128, 0, ApplyWater: true), new EditSession("stamp"));

        Assert.Equal(20, H(world, 256, 256), 1);
        Assert.Equal(40, H(world, 50, 50), 1);
        Assert.Equal(StampOp.DefaultWaterLevel, world.Water![256, 256]);
        Assert.Equal(0, world.Water[50, 50]);
    }

    [Fact]
    public void New_world_has_every_layer_and_round_trips()
    {
        var world = NewWorldGenerator.Create(
            new NewWorldOptions("Fresh", 1024, Edges: [EdgeStyle.None, EdgeStyle.None, EdgeStyle.None, EdgeStyle.None]),
            StampLibrary.Empty, levelTemplate: [.. "ttw\0"u8, 32, .. "00000000000000000000000000000000"u8]);

        Assert.NotNull(world.Biomes);
        Assert.NotNull(world.Roads);
        Assert.NotNull(world.Water);
        Assert.Equal(1024 / 32, world.Radiation!.Width);
        Assert.Null(WorldXml.GetProperty(world.MapInfo, "GameVersion"));

        WorldWriter.Write(world, _dir);
        var loaded = WorldReader.Read(_dir);
        Assert.Empty(WorldComparer.Compare(world, loaded));
        Assert.Empty(loaded.Warnings);
    }

    [Fact]
    public void A_default_spawn_lands_on_dry_ground_near_the_centre()
    {
        var world = Flat(512, 20);
        world.Water = new Grid<byte>(512, 512);
        // Flood the middle so the spawn has to move out to dry land.
        for (int z = 200; z < 312; z++)
            for (int x = 200; x < 312; x++) world.Water[x, z] = 30;
        for (int z = 0; z < 512; z++)
            for (int x = 0; x < 512; x++)
                if (x < 200 || x >= 312 || z < 200 || z >= 312) world.Height[x, z] = World.MetresToHeight(45);

        var spawn = SpawnHelper.EnsureOne(world);

        Assert.NotNull(spawn);
        int gx = world.WorldToGrid((int)spawn.X), gz = world.WorldToGrid((int)spawn.Z);
        Assert.True(gx < 200 || gx >= 312 || gz < 200 || gz >= 312, "spawn should be out of the water");
        Assert.Equal(46, spawn.Y);
        Assert.Null(SpawnHelper.EnsureOne(world));
    }

    [Fact]
    public void Heightmap_export_import_round_trips_as_png_and_raw()
    {
        var world = Flat(512);
        TerrainBrush.Dab(world.Height, 30, 400, new BrushSettings(BrushMode.Raise, 25, 1), new EditSession("r"));

        foreach (var ext in new[] { ".png", ".raw" })
        {
            string path = Path.Combine(_dir, "height" + ext);
            HeightmapFile.Export(world.Height, path);
            Assert.Equal(world.Height.Data, HeightmapFile.Import(path, 512).Data);
        }
        Assert.Throws<InvalidDataException>(() => HeightmapFile.Import(Path.Combine(_dir, "height.png"), 1024));
    }

    [Fact]
    public void Game_stamps_and_borders_work_when_an_install_is_available()
    {
        var install = GameInstall.FindCandidates().FirstOrDefault();
        if (install is null) return;
        var stamps = StampLibrary.Load(GameInstall.Open(install));
        Assert.Contains(stamps.Names, n => n.StartsWith("mountains_"));

        var mountains = stamps.Get(stamps.WithPrefix("mountains_")[0]);
        Assert.Equal(512, mountains.Size);
        // Game terrain stamps keep a constant height (255 m) and carry their shape in alpha.
        Assert.Equal(255, mountains.HeightConst, 1);
        Assert.NotNull(mountains.Alpha);

        var world = NewWorldGenerator.Create(
            new NewWorldOptions("Borders", 2048, NoiseAmplitude: 0, Edges: [EdgeStyle.Water, EdgeStyle.Land, EdgeStyle.Land, EdgeStyle.Land]),
            stamps, levelTemplate: null);
        Assert.True(world.Water!.Data.Any(v => v > 0), "the water edge should hold water");
        Assert.True(world.Water[1024, 2047] > 0, "the north edge should be ocean");
        Assert.Equal(0, world.Water[1024, 1024]);
    }
}
