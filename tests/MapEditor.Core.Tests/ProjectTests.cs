using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Validate;

namespace MapEditor.Core.Tests;

public sealed class ProjectTests : IDisposable
{
    private const int Size = 512;
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-project-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static World MakeWorld()
    {
        var world = new World { Name = "Proj", Size = Size, Height = new Grid<ushort>(Size, Size) };
        for (int i = 0; i < world.Height.Data.Length; i++) world.Height.Data[i] = (ushort)(i * 7);
        var biomes = new Grid<byte>(Size / 8, Size / 8);
        biomes[3, 4] = 1;
        world.Biomes = new IndexedLayer(biomes, [0x004000, 0xFFE477]);
        world.Roads = new Grid<byte>(Size, Size);
        world.Roads[5, 6] = (byte)RoadType.Gravel;
        world.Water = new Grid<byte>(Size, Size);
        world.Water[7, 8] = 30;
        world.Radiation = new Grid<byte>(Size / 32, Size / 32);
        world.Radiation[1, 1] = 255;
        var prefab = new PrefabInstance { Name = "house_01", Position = new Int3(-10, 37, 20), Rotation = 3, Type = "model" };
        prefab.ExtraAttributes.Add(new("help", "x"));
        world.Prefabs.Add(prefab);
        world.Spawns.Add(new SpawnPoint { X = 1.5f, Y = 40, Z = -7, RotationY = 90 });
        world.ExtraFiles[WorldFiles.Level] = [1, 2, 3];
        WorldXml.SetProperty(world.MapInfo, "Description", "test");
        return world;
    }

    [Fact]
    public void Project_round_trips_everything()
    {
        var world = MakeWorld();
        string path = Path.Combine(_dir, "a" + ProjectStore.Extension);
        ProjectStore.Save(world, path, new ProjectStore.ProjectSettings { ExportFolder = @"C:\out\Proj" });

        var copy = ProjectStore.Load(path, out var settings);
        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Equal("Proj", copy.Name);
        Assert.Equal(world.Height.Data, copy.Height.Data);
        Assert.Equal(@"C:\out\Proj", settings.ExportFolder);
        Assert.Equal("help", copy.Prefabs[0].ExtraAttributes[0].Key);
        Assert.Equal("test", WorldXml.GetProperty(copy.MapInfo, "Description"));
        Assert.Equal([1, 2, 3], copy.ExtraFiles[WorldFiles.Level]);
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Passthrough_image_is_kept_until_its_layer_is_edited()
    {
        var world = MakeWorld();
        world.PassthroughImages[WorldFiles.Roads] = new RgbaImage(2, 1, [1, 2, 3, 4, 5, 6, 7, 8]);
        string path = Path.Combine(_dir, "b" + ProjectStore.Extension);

        ProjectStore.Save(world, path);
        Assert.True(ProjectStore.Load(path).PassthroughImages.ContainsKey(WorldFiles.Roads));

        world.Roads!.MarkChanged();
        ProjectStore.Save(world, path);
        Assert.False(ProjectStore.Load(path).PassthroughImages.ContainsKey(WorldFiles.Roads));
    }

    [Fact]
    public void Clone_is_independent_and_keeps_edit_versions()
    {
        var world = MakeWorld();
        world.Roads!.MarkChanged();
        var copy = world.Clone();

        Assert.Empty(WorldComparer.Compare(world, copy));
        Assert.Equal(1, copy.Roads!.Version);
        Assert.Equal(0, copy.Water!.Version);

        world.Height[0, 0] = 1234;
        world.Prefabs[0].Position = new Int3(0, 0, 0);
        world.Spawns.Clear();
        Assert.NotEqual(1234, copy.Height[0, 0]);
        Assert.Equal(new Int3(-10, 37, 20), copy.Prefabs[0].Position);
        Assert.Single(copy.Spawns);
    }

    [Fact]
    public void Not_a_project_is_rejected()
    {
        string path = Path.Combine(_dir, "bad" + ProjectStore.Extension);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.ThrowsAny<InvalidDataException>(() => ProjectStore.Load(path));
    }

    private static PrefabInfo Poi(string name, int s) => new(name, "POIs", new Int3(s, 10, s), -1, 0, 1, [], null);

    [Fact]
    public void Validator_flags_spawns_traders_overlaps_water_and_unknown_prefabs()
    {
        var world = new World { Name = "V", Size = Size, Height = new Grid<ushort>(Size, Size) };
        Array.Fill(world.Height.Data, World.MetresToHeight(20));
        world.Water = new Grid<byte>(Size, Size);
        for (int z = 300; z < 340; z++) for (int x = 300; x < 340; x++) world.Water[x, z] = 25;
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [0x004000]);
        world.ExtraFiles[WorldFiles.Level] = [1];

        var library = PrefabLibrary.Of([Poi("a", 30), Poi("b", 30), Poi("wet", 30)]);
        world.Prefabs.Add(new PrefabInstance { Name = "a", Position = new Int3(-200, 20, -200) });
        world.Prefabs.Add(new PrefabInstance { Name = "b", Position = new Int3(-190, 20, -190) });
        world.Prefabs.Add(new PrefabInstance { Name = "wet", Position = new Int3(50, 20, 50) });
        world.Prefabs.Add(new PrefabInstance { Name = "missing", Position = new Int3(0, 20, 0) });
        world.Prefabs.Add(new PrefabInstance { Name = "a", Position = new Int3(400, 20, 0) });

        var issues = WorldValidator.Validate(world, library);
        string all = string.Join("\n", issues.Select(i => i.Message));

        Assert.Contains("no spawn points", all);
        Assert.Contains("no trader", all);
        Assert.Contains("a overlaps b", all);
        Assert.Contains("wet is 100% under water", all);
        Assert.Contains("missing (1×) is not in the game folder", all);
        Assert.Contains(issues, i => i.Severity == IssueSeverity.Error && i.Message.StartsWith("a is outside"));
        var overlap = issues.Single(i => i.Message.Contains("overlaps"));
        Assert.Equal((76, 76), (overlap.X!.Value, overlap.Z!.Value));
    }

    [Fact]
    public void Validator_is_quiet_for_a_tidy_world()
    {
        var world = new World { Name = "V", Size = Size, Height = new Grid<ushort>(Size, Size) };
        world.Biomes = new IndexedLayer(new Grid<byte>(Size / 8, Size / 8), [0x004000]);
        world.ExtraFiles[WorldFiles.Level] = [1];
        world.Spawns.Add(new SpawnPoint { X = 0, Y = 5, Z = 0 });
        var trader = new PrefabInfo("trader_hugh", "POIs", new Int3(40, 10, 40), -1, 0, 1, [], null);
        world.Prefabs.Add(new PrefabInstance { Name = "trader_hugh", Position = new Int3(0, 5, 0) });

        Assert.Empty(WorldValidator.Validate(world, PrefabLibrary.Of([trader])));
    }
}
