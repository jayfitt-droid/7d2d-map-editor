using MapEditor.Core.IO;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class WorldIoTests : IDisposable
{
    private const int Size = 512;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-world-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld()
    {
        var world = new World { Name = "Test", Size = Size, Height = new Grid<ushort>(Size, Size) };
        for (int z = 0; z < Size; z++)
            for (int x = 0; x < Size; x++)
                world.Height[x, z] = World.MetresToHeight(40 + x * 0.1f + z * 0.05f);

        var biomes = new Grid<byte>(Size / 8, Size / 8);
        biomes[0, 0] = 1;
        world.Biomes = new IndexedLayer(biomes, [0x004000, 0xFFE477]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Roads[10, 0] = (byte)RoadType.Asphalt;
        world.Roads[11, 0] = (byte)RoadType.Gravel;
        world.Water = new Grid<byte>(Size, Size);
        world.Water[0, 0] = 30;
        world.Radiation = new Grid<byte>(Size / 32, Size / 32);
        world.Radiation[0, 15] = 255;
        world.Prefabs.Add(new PrefabInstance { Name = "abandoned_house_01", Position = new Int3(-100, 37, 20), Rotation = 2 });
        world.Spawns.Add(new SpawnPoint { X = 12.5f, Y = 40, Z = -7, RotationY = 90 });
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        return world;
    }

    [Fact]
    public void World_round_trips_through_game_files()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        var copy = WorldReader.Read(_dir);

        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Empty(copy.Warnings);
        Assert.Equal($"{Size},{Size}", WorldXml.GetProperty(copy.MapInfo, "HeightMapSize"));
    }

    [Fact]
    public void Pngs_are_written_north_up_and_raw_south_up()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);

        // Grid row 0 (south) must be the LAST image row in the PNG.
        var water = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Water), out _, out _);
        int lastRow = (Size - 1) * Size * 4;
        Assert.Equal(30, water[lastRow + 2]);
        Assert.Equal(0, water[2]);

        var roads = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Roads), out _, out _);
        Assert.Equal([255, 0, 0, 255], roads.AsSpan(lastRow + 10 * 4, 4).ToArray());
        Assert.Equal([0, 255, 0, 255], roads.AsSpan(lastRow + 11 * 4, 4).ToArray());

        // radiation[0, 15] is the top (north) row of a 16x16 image.
        var rad = Png.ReadRgba(Path.Combine(_dir, WorldFiles.Radiation), out _, out _);
        Assert.Equal(255, rad[0]);

        // dtm.raw row 0 is south: its first sample is height at x=0, z=0.
        var raw = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Height));
        Assert.Equal(world.Height[0, 0], BitConverter.ToUInt16(raw, 0));
    }

    [Fact]
    public void Non_rwg_road_colours_survive_until_edited()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);

        // Paint a colour RWG never writes into splat3.
        string roadsPath = Path.Combine(_dir, WorldFiles.Roads);
        var pixels = Png.ReadRgba(roadsPath, out int w, out int h);
        pixels[0] = 10; pixels[1] = 20; pixels[2] = 30; pixels[3] = 40;
        Png.WriteRgba(roadsPath, w, h, pixels);

        var loaded = WorldReader.Read(_dir);
        Assert.Contains(loaded.Warnings, m => m.Contains("splat3"));

        string outDir = Path.Combine(_dir, "out");
        WorldWriter.Write(loaded, outDir);
        Assert.Equal(pixels, Png.ReadRgba(Path.Combine(outDir, WorldFiles.Roads), out _, out _));
    }

    [Fact]
    public void Derived_caches_are_removed_on_overwrite()
    {
        var world = MakeWorld();
        WorldWriter.Write(world, _dir);
        File.WriteAllText(Path.Combine(_dir, "checksums.txt"), "stale");
        File.WriteAllText(Path.Combine(_dir, "splat3_processed.png"), "stale");

        Assert.Throws<IOException>(() => WorldWriter.Write(world, _dir));
        WorldWriter.Write(world, _dir, new WorldWriteOptions { Overwrite = true });

        Assert.False(File.Exists(Path.Combine(_dir, "checksums.txt")));
        Assert.False(File.Exists(Path.Combine(_dir, "splat3_processed.png")));
    }

    [Fact]
    public void Missing_level_file_needs_a_template()
    {
        var world = MakeWorld();
        world.ExtraFiles.Remove(WorldFiles.Level);
        Assert.Throws<InvalidOperationException>(() => WorldWriter.Write(world, _dir));

        string outDir = Path.Combine(_dir, "templated");
        WorldWriter.Write(world, outDir, new WorldWriteOptions { LevelTemplate = [9, 9] });
        Assert.Equal([9, 9], File.ReadAllBytes(Path.Combine(outDir, WorldFiles.Level)));
    }

    [Fact]
    public void New_level_guid_replaces_only_the_guid()
    {
        var world = MakeWorld();
        byte[] ttw = [.. "ttw\0"u8, 1, 2, 3, 32, .. "A6BA93844F7C91B4E9667E128C5C0BAC"u8];
        world.ExtraFiles[WorldFiles.Level] = ttw;

        WorldWriter.Write(world, _dir, new WorldWriteOptions { NewLevelGuid = true });

        var written = File.ReadAllBytes(Path.Combine(_dir, WorldFiles.Level));
        Assert.Equal(ttw.Length, written.Length);
        Assert.Equal(ttw[..^32], written[..^32]);
        Assert.NotEqual(ttw[^32..], written[^32..]);
        Assert.True(LevelFile.EndsWithGuid(written));
        Assert.Equal(written, world.ExtraFiles[WorldFiles.Level]);
    }

    /// <summary>Set MAPEDITOR_TEST_WORLD to a real world folder to run this.</summary>
    [Fact]
    public void Real_world_round_trips_when_configured()
    {
        string? folder = Environment.GetEnvironmentVariable("MAPEDITOR_TEST_WORLD");
        if (string.IsNullOrEmpty(folder)) return;

        var world = WorldReader.Read(folder);
        string outDir = Path.Combine(_dir, "real");
        WorldWriter.Write(world, outDir);
        Assert.Empty(WorldComparer.Compare(world, WorldReader.Read(outDir)));
        Assert.Equal(File.ReadAllBytes(Path.Combine(folder, WorldFiles.Height)), File.ReadAllBytes(Path.Combine(outDir, WorldFiles.Height)));
    }
}
