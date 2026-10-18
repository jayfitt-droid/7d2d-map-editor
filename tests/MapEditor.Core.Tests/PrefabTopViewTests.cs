using System.Text;
using MapEditor.Core.GameData;
using MapEditor.Core.Model;

namespace MapEditor.Core.Tests;

public sealed class PrefabTopViewTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("mapeditor-topview-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Writes a .tts (blocks x fastest, then y, then z) and its .blocks.nim name map.</summary>
    private string WritePrefab(int sx, int sy, int sz, Func<int, int, int, int> block, Dictionary<int, string> names, uint version = 19)
    {
        string tts = Path.Combine(_dir, "test.tts");
        using (var w = new BinaryWriter(File.Create(tts)))
        {
            w.Write(Encoding.ASCII.GetBytes("tts\0"));
            w.Write(version);
            w.Write((short)sx);
            w.Write((short)sy);
            w.Write((short)sz);
            for (int z = 0; z < sz; z++)
                for (int y = 0; y < sy; y++)
                    for (int x = 0; x < sx; x++)
                        w.Write((uint)block(x, y, z));
        }
        using (var w = new BinaryWriter(File.Create(Path.Combine(_dir, "test.blocks.nim")), Encoding.UTF8))
        {
            w.Write(1);
            w.Write(names.Count);
            foreach (var (id, name) in names)
            {
                w.Write(id);
                w.Write(name);
            }
        }
        return tts;
    }

    private static readonly Dictionary<int, string> Names = new()
    {
        [0] = "air", [1] = "terrDirt", [2] = "terrAsphalt", [3] = "brickShapes:cube", [4] = "treeOak", [5] = "terrainFiller",
    };

    [Fact]
    public void The_highest_visible_block_of_each_column_shows_and_props_are_looked_through()
    {
        // 4 x 4 x 4: dirt at y 0, an asphalt strip along x = 1, a brick block at (3, 0), a tree on dirt at (0, 3).
        var tts = WritePrefab(4, 4, 4, (x, y, z) => (x, y, z) switch
        {
            (_, 0, _) => 1,
            (1, 1, _) => 2,
            (3, < 3, 0) => 3,
            (0, 1, 3) => 4,
            _ => 0,
        }, Names);
        var view = PrefabTopViewReader.Read(tts, yOffset: -1)!;
        Assert.Equal((4, 4), (view.Width, view.Height));

        uint At(int x, int z) => view.Pixels[(view.Height - 1 - z) * view.Width + x]; // row 0 is north
        uint dirt = At(0, 0), asphalt = At(1, 0), brick = At(3, 0);
        Assert.NotEqual(0u, dirt);
        Assert.NotEqual(dirt, asphalt);
        Assert.NotEqual(dirt, brick);
        Assert.Equal(asphalt, At(1, 3));
        Assert.Equal(dirt, At(0, 3)); // the tree is looked through
    }

    [Fact]
    public void Terrain_filler_leaves_the_ground_showing()
    {
        var tts = WritePrefab(2, 2, 2, (x, y, z) => y == 0 ? 5 : 0, Names);
        Assert.All(PrefabTopViewReader.Read(tts, 0)!.Pixels, p => Assert.Equal(0u, p));
    }

    [Fact]
    public void Older_files_use_15_bit_block_types()
    {
        // Bit 15 belonged to the rotation before version 18, so it must not change the block type.
        var tts = WritePrefab(1, 1, 1, (_, _, _) => 2 | 1 << 15, Names, version: 17);
        Assert.NotEqual(0u, PrefabTopViewReader.Read(tts, 0)!.Pixels[0]);
    }

    [Fact]
    public void Unreadable_files_give_no_view()
    {
        string tts = Path.Combine(_dir, "bad.tts");
        File.WriteAllText(tts, "nope");
        File.WriteAllText(Path.Combine(_dir, "bad.blocks.nim"), "");
        Assert.Null(PrefabTopViewReader.Read(tts, 0));
        Assert.Null(PrefabTopViewReader.Read(Path.Combine(_dir, "missing.tts"), 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Views_turn_the_same_way_as_markers(int rotation)
    {
        // A 3 x 2 view with one lit block in each corner cell of a marker; both must land in the same place.
        var size = new Int3(3, 1, 2);
        var marker = new PrefabMarker(new Int3(2, 0, 0), new Int3(1, 1, 1), MarkerType.POISpawn, "1");
        var pixels = new uint[6];
        pixels[(2 - 1 - 0) * 3 + 2] = 0xFF000001; // x 2, z 0 (rows run from the north)
        var view = new PrefabTopView(3, 2, pixels).Turned(rotation);

        var info = new PrefabInfo("p", "", size, 0, 0, 0, [], null) { Markers = [marker] };
        var turned = info.RotatedMarkers(rotation)[0].Start;
        Assert.Equal(0xFF000001u, view.Pixels[(view.Height - 1 - turned.Z) * view.Width + turned.X]);
    }
}
