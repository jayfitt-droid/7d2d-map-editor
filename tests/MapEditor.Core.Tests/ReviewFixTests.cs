using System.IO.Compression;
using System.Text.Json.Nodes;
using MapEditor.Core.Edit;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;

namespace MapEditor.Core.Tests;

/// <summary>Cases from the code review of v1.1.1: failed writes, stale layers, crafted projects, passthrough images.</summary>
public sealed class ReviewFixTests : IDisposable
{
    private const int Size = 64;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-review-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld(float metres = 40)
    {
        var world = new World { Name = "R", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(metres));
        world.Water = new Grid<byte>(Size, Size);
        world.Water[1, 1] = 30;
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        return world;
    }

    private string Folder(string name) => Path.Combine(_dir, name);

    [Fact]
    public void A_failed_write_leaves_the_existing_world_as_it_was()
    {
        string folder = Folder("world");
        WorldWriter.Write(MakeWorld(40), folder);
        var before = Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);

        // Another program holds prefabs.xml open, so it can't be replaced.
        using (new FileStream(Path.Combine(folder, WorldFiles.Prefabs), FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.ThrowsAny<IOException>(() => WorldWriter.Write(MakeWorld(55), folder, new WorldWriteOptions { Overwrite = true }));

        var after = Directory.GetFiles(folder).ToDictionary(f => Path.GetFileName(f), File.ReadAllBytes);
        Assert.Equal(before.Keys.Order(), after.Keys.Order());
        foreach (var (name, bytes) in before) Assert.Equal(bytes, after[name]);
        Assert.Empty(Directory.GetDirectories(folder));
    }

    [Fact]
    public void A_write_that_fails_before_writing_touches_nothing()
    {
        string folder = Folder("world");
        WorldWriter.Write(MakeWorld(40), folder);
        var dtm = File.ReadAllBytes(Path.Combine(folder, WorldFiles.Height));

        var noLevel = MakeWorld(55);
        noLevel.ExtraFiles.Clear();
        Assert.Throws<InvalidOperationException>(() => WorldWriter.Write(noLevel, folder, new WorldWriteOptions { Overwrite = true }));
        Assert.Equal(dtm, File.ReadAllBytes(Path.Combine(folder, WorldFiles.Height)));
    }

    [Fact]
    public void Overwriting_removes_layers_the_world_no_longer_has_and_stale_caches()
    {
        string folder = Folder("world");
        WorldWriter.Write(MakeWorld(), folder);
        File.WriteAllText(Path.Combine(folder, "checksums.txt"), "old");
        File.WriteAllText(Path.Combine(folder, "notes.txt"), "the user's own file");

        var dry = MakeWorld();
        dry.Water = null;
        WorldWriter.Write(dry, folder, new WorldWriteOptions { Overwrite = true });

        Assert.False(File.Exists(Path.Combine(folder, WorldFiles.Water)));
        Assert.False(File.Exists(Path.Combine(folder, "checksums.txt")));
        Assert.True(File.Exists(Path.Combine(folder, "notes.txt")));
        Assert.Null(WorldReader.Read(folder).Water);
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("sub/file.txt")]
    [InlineData(@"C:\escaped.txt")]
    [InlineData("dtm.raw")]
    [InlineData("NUL.txt")]
    public void Extra_files_must_be_plain_names(string name)
    {
        var world = MakeWorld();
        world.ExtraFiles[name] = [9];
        string folder = Folder("out");
        Assert.Throws<InvalidOperationException>(() => WorldWriter.Write(world, folder));
        Assert.False(File.Exists(Path.Combine(_dir, "escaped.txt")));
        Assert.False(Directory.Exists(folder));
    }

    /// <summary>Saves a valid project, then edits its project.json as someone crafting a file might.</summary>
    private string CraftedProject(Action<JsonObject> edit)
    {
        string path = Path.Combine(_dir, "p" + ProjectStore.Extension);
        ProjectStore.Save(MakeWorld(), path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        var entry = zip.GetEntry("project.json")!;
        JsonObject json;
        using (var s = entry.Open()) json = JsonNode.Parse(s)!.AsObject();
        edit(json);
        entry.Delete();
        using (var w = new StreamWriter(zip.CreateEntry("project.json").Open())) w.Write(json.ToJsonString());
        return path;
    }

    [Fact]
    public void A_project_with_an_escaping_extra_file_is_refused()
    {
        string path = CraftedProject(j => j["ExtraFiles"] = new JsonArray("../escaped.txt"));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void A_project_whose_image_size_overflows_is_refused()
    {
        // 65537 × 16384 × 4 wraps to 65536 in 32-bit arithmetic.
        string path = CraftedProject(j => j["Passthrough"] = new JsonArray(new JsonObject
        {
            ["File"] = WorldFiles.Roads, ["Width"] = 65537, ["Height"] = 16384,
        }));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }

    [Fact]
    public void A_project_with_an_impossible_world_size_is_refused()
    {
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(CraftedProject(j => j["Size"] = 1_000_000)));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Load(CraftedProject(j => j["Radiation"] = new JsonObject { ["Width"] = 99999, ["Height"] = 2 })));
    }

    [Fact]
    public void Png_writer_checks_the_buffer_without_overflowing()
    {
        Assert.Throws<ArgumentException>(() => Png.WriteRgba(Path.Combine(_dir, "x.png"), 65537, 16384, new byte[65536]));
    }

    [Fact]
    public void Png_chunk_longer_than_the_file_is_refused()
    {
        string path = Path.Combine(_dir, "small.png");
        Png.WriteRgba(path, 2, 2, new byte[16]);
        var bytes = File.ReadAllBytes(path);
        // The first chunk after the 8-byte signature is IHDR; claim it is 1 GB long.
        bytes[8] = 0x40;
        File.WriteAllBytes(path, bytes);
        Assert.Throws<InvalidDataException>(() => Png.ReadRgba(path, out _, out _));
    }

    [Fact]
    public void Painting_a_biome_map_kept_as_its_original_is_refused()
    {
        var world = MakeWorld();
        world.PassthroughImages[WorldFiles.Biomes] = new RgbaImage(8, 8, new byte[8 * 8 * 4]);
        Assert.NotNull(LayerPaint.CantPaintBiome(world, LayerPaint.DefaultBiome));

        var area = LayerPaint.PaintBiome(world, 32, 32, new BrushTip(10), LayerPaint.DefaultBiome, new EditSession("Paint"));
        Assert.True(area.IsEmpty);
        Assert.Null(world.Biomes);
    }

    [Fact]
    public void Edit_then_undo_keeps_the_original_road_image()
    {
        // A splat3 with a colour RWG never writes, so it is kept as a passthrough image.
        string folder = Folder("world");
        var source = MakeWorld();
        WorldWriter.Write(source, folder);
        var rgba = new byte[Size * Size * 4];
        rgba[0] = 12; rgba[1] = 34; rgba[2] = 56; rgba[3] = 255;
        Png.WriteRgba(Path.Combine(folder, WorldFiles.Roads), Size, Size, rgba);
        var world = WorldReader.Read(folder);
        Assert.True(world.PassthroughImages.ContainsKey(WorldFiles.Roads));

        var stack = new UndoStack();
        var session = new EditSession("Road");
        session.Touch(world.Roads!, new GridRect(0, 0, 8, 8));
        world.Roads![3, 3] = (byte)RoadType.Asphalt;
        world.Roads.MarkChanged();
        stack.Push(session);
        stack.Undo();
        Assert.NotEqual(0, world.Roads.Version);

        string again = Folder("again");
        WorldWriter.Write(world, again);
        Assert.Equal(rgba, Png.ReadRgba(Path.Combine(again, WorldFiles.Roads), out _, out _));
    }
}
