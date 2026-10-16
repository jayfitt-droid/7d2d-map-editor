using System.Diagnostics;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;

return args switch
{
    ["info", var world] => Info(world),
    ["roundtrip", var world, var output, .. var rest] => RoundTrip(world, output, overwrite: rest.Contains("--overwrite")),
    ["installs"] => Installs(),
    ["render", var world, var output, .. var rest] => Render(world, output, rest.Length > 0 ? int.Parse(rest[0]) : -1),
    ["new", var output, .. var rest] => New(output, rest.Length > 0 ? int.Parse(rest[0]) : 4096, rest.Length > 1 ? rest[1] : "LLLL"),
    _ => Usage(),
};

static int Usage()
{
    Console.Error.WriteLine("""
        mapeditor-cli info <worldFolder>
            Print a summary of a game world.
        mapeditor-cli roundtrip <worldFolder> <outFolder> [--overwrite]
            Load a world, write it back out, re-load the output and compare every layer.
        mapeditor-cli installs
            List detected 7 Days to Die installs.
        mapeditor-cli render <worldFolder> <out.png> [level]
            Render the editor's map view (default: 2 levels below the top) to a PNG.
        mapeditor-cli new <outFolder> [size] [edges]
            Create a blank world. edges = 4 letters for N,E,S,W: L land, W water, N none (default LLLL).
        """);
    return 2;
}

static int New(string output, int size, string edges)
{
    var installPath = GameInstall.FindCandidates().FirstOrDefault();
    var install = installPath is null ? null : GameInstall.Open(installPath);
    var stamps = install is null ? StampLibrary.Empty : StampLibrary.Load(install);
    var template = LevelFile.FindTemplate(install);
    if (template is null)
    {
        Console.Error.WriteLine("No main.ttw template found; generate any world in the game once first.");
        return 1;
    }

    var styles = edges.ToUpperInvariant().Select(c => c switch { 'W' => EdgeStyle.Water, 'N' => EdgeStyle.None, _ => EdgeStyle.Land }).ToArray();
    var sw = Stopwatch.StartNew();
    var world = NewWorldGenerator.Create(new NewWorldOptions(Path.GetFileName(output), size, Seed: Environment.TickCount, Edges: styles),
        stamps, File.ReadAllBytes(template.Path));
    WorldWriter.Write(world, output);
    Console.WriteLine($"Created {size}x{size} world in {sw.Elapsed.TotalSeconds:0.0} s (main.ttw from {template.Path}, {template.GameVersion})");
    return 0;
}

static int Render(string folder, string output, int level)
{
    var world = WorldReader.Read(folder);
    var install = GameInstall.FindCandidates().FirstOrDefault();
    var biomes = install is null ? BiomeTable.Vanilla : BiomeTable.Load(GameInstall.Open(install));
    var renderer = new MapRenderer(world, biomes);
    if (level < 0) level = Math.Max(0, renderer.Levels - 3);

    int tiles = renderer.TilesAcross(level);
    int px = (int)Math.Ceiling(world.Size / (double)MapRenderer.MetresPerPixel(level));
    var image = new byte[px * px * 4];
    var sw = Stopwatch.StartNew();
    Parallel.For(0, tiles * tiles, i =>
    {
        int tx = i % tiles, ty = i / tiles;
        var tile = new uint[MapRenderer.TileSize * MapRenderer.TileSize];
        renderer.RenderTile(level, tx, ty, MapLayers.All, tile);
        for (int y = 0; y < MapRenderer.TileSize; y++)
        {
            int iy = ty * MapRenderer.TileSize + y;
            if (iy >= px) break;
            for (int x = 0; x < MapRenderer.TileSize; x++)
            {
                int ix = tx * MapRenderer.TileSize + x;
                if (ix >= px) break;
                uint c = tile[y * MapRenderer.TileSize + x];
                int o = (iy * px + ix) * 4;
                image[o] = (byte)(c >> 16);
                image[o + 1] = (byte)(c >> 8);
                image[o + 2] = (byte)c;
                image[o + 3] = (byte)(c >> 24);
            }
        }
    });
    Console.WriteLine($"level {level}: {tiles}x{tiles} tiles, {px}x{px} px in {sw.Elapsed.TotalSeconds:0.00} s");
    Png.WriteRgba(output, px, px, image);
    return 0;
}

static int Info(string folder)
{
    var sw = Stopwatch.StartNew();
    var world = WorldReader.Read(folder);
    Console.Write(WorldSummaryFormatter.Format(world));
    Console.WriteLine($"Loaded in  {sw.Elapsed.TotalSeconds:0.00} s");
    return 0;
}

static int RoundTrip(string folder, string output, bool overwrite)
{
    var sw = Stopwatch.StartNew();
    var original = WorldReader.Read(folder);
    Console.WriteLine($"read   {sw.Elapsed.TotalSeconds,6:0.00} s");

    sw.Restart();
    WorldWriter.Write(original, output, new WorldWriteOptions { Overwrite = overwrite });
    Console.WriteLine($"write  {sw.Elapsed.TotalSeconds,6:0.00} s");

    sw.Restart();
    var copy = WorldReader.Read(output);
    Console.WriteLine($"reread {sw.Elapsed.TotalSeconds,6:0.00} s");

    var differences = WorldComparer.Compare(original, copy);
    foreach (var d in differences) Console.WriteLine("DIFF   " + d);
    Console.WriteLine(differences.Count == 0 ? "OK     all layers identical" : $"FAIL   {differences.Count} difference(s)");
    return differences.Count == 0 ? 0 : 1;
}

static int Installs()
{
    var found = GameInstall.FindCandidates();
    foreach (var path in found) Console.WriteLine(path);
    if (found.Count == 0) Console.WriteLine("No Steam installs found; pick the game folder manually.");
    Console.WriteLine("Generated worlds: " + GameInstall.UserGeneratedWorldsDir);
    return 0;
}
