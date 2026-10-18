using System.Diagnostics;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;
using MapEditor.Core.Ops;
using MapEditor.Core.Rendering;
using MapEditor.Core.Validate;

return args switch
{
    ["info", var world] => Info(world),
    ["roundtrip", var world, var output, .. var rest] => RoundTrip(world, output, overwrite: rest.Contains("--overwrite")),
    ["installs"] => Installs(),
    ["render", var world, var output, .. var rest] => Render(world, output, rest.Length > 0 ? int.Parse(rest[0]) : -1),
    ["export", var project, var output, .. var rest] => Export(project, output, overwrite: rest.Contains("--overwrite")),
    ["validate", var source] => Validate(source),
    ["compare", var a, var b] => Compare(a, b),
    ["topview", var output, .. var names] when names.Length > 0 => TopView(output, names),
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
        mapeditor-cli export <project.7dmap> <outFolder> [--overwrite]
            Write a project as a game world folder.
        mapeditor-cli validate <worldFolder|project.7dmap>
            List problems: spawns, traders, prefab overlaps, water and bounds.
        mapeditor-cli compare <worldFolder|project.7dmap> <worldFolder|project.7dmap>
            Compare every layer, prefab and spawn of two worlds.
        mapeditor-cli topview <out.png> <prefab>[:rotation] [...]
            Draw prefabs from above, as the map's prefab detail shows them, side by side (4 px per block).
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

static int TopView(string output, string[] names)
{
    var installPath = GameInstall.FindCandidates().FirstOrDefault();
    if (installPath is null)
    {
        Console.Error.WriteLine("No game install found.");
        return 1;
    }
    var library = PrefabLibrary.Load(GameInstall.Open(installPath));
    const int scale = 4, gap = 8;
    var views = new List<PrefabTopView>();
    foreach (var arg in names)
    {
        var parts = arg.Split(':');
        if (library.Find(parts[0]) is not { } info)
        {
            Console.Error.WriteLine($"No prefab named {parts[0]}");
            return 1;
        }
        var sw = Stopwatch.StartNew();
        var view = PrefabTopViewReader.Read(Path.Combine(info.Folder, info.Name + ".tts"), info.YOffset);
        if (view is null)
        {
            Console.Error.WriteLine($"Could not read {info.Name}");
            return 1;
        }
        Console.WriteLine($"{info.Name}: {info.Size.X}x{info.Size.Y}x{info.Size.Z} in {sw.ElapsedMilliseconds} ms");
        var turned = view.Turned(parts.Length > 1 ? int.Parse(parts[1]) : 0);
        // The street a link road would match at each edge.
        string[] edges = ["N", "E", "S", "W"];
        Console.WriteLine("  streets: " + string.Join(", ", Enumerable.Range(0, 4).Select(d =>
            TownGenerator.StreetAcross(turned, d) is { } s ? $"{edges[d]} {s.Type} {s.Radius * 2:0.#} m" : $"{edges[d]} none")));
        views.Add(turned);
    }
    int width = views.Sum(v => v.Width * scale + gap), height = views.Max(v => v.Height) * scale;
    var image = new byte[width * height * 4];
    int left = 0;
    foreach (var v in views)
    {
        for (int y = 0; y < v.Height * scale; y++)
            for (int x = 0; x < v.Width * scale; x++)
            {
                uint c = v.Pixels[y / scale * v.Width + x / scale];
                int o = (y * width + left + x) * 4;
                (image[o], image[o + 1], image[o + 2], image[o + 3]) = c == 0 ? ((byte)40, (byte)90, (byte)40, (byte)255)
                    : ((byte)(c >> 16), (byte)(c >> 8), (byte)c, (byte)255);
            }
        left += v.Width * scale + gap;
    }
    Png.WriteRgba(output, width, height, image);
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

static World Load(string source) =>
    File.Exists(source) && source.EndsWith(ProjectStore.Extension, StringComparison.OrdinalIgnoreCase)
        ? ProjectStore.Load(source)
        : WorldReader.Read(source);

static int Export(string project, string output, bool overwrite)
{
    var sw = Stopwatch.StartNew();
    var world = ProjectStore.Load(project);
    world.Name = Path.GetFileName(Path.GetFullPath(output));
    WorldWriter.Write(world, output, new WorldWriteOptions { Overwrite = overwrite, NewLevelGuid = true });
    Console.WriteLine($"Exported {world.Name} in {sw.Elapsed.TotalSeconds:0.0} s");
    return 0;
}

static int Validate(string source)
{
    var world = Load(source);
    var installPath = GameInstall.FindCandidates().FirstOrDefault();
    var library = installPath is null ? PrefabLibrary.Empty : PrefabLibrary.Load(GameInstall.Open(installPath));
    var issues = WorldValidator.Validate(world, library);
    foreach (var i in issues)
        Console.WriteLine($"{i.Severity,-8} {i.Message}{(i.HasLocation ? $"  (at {world.GridToWorld(i.X!.Value)}, {world.GridToWorld(i.Z!.Value)})" : "")}");
    Console.WriteLine(issues.Count == 0 ? "No problems found." : $"{issues.Count} issue(s).");
    return issues.Any(i => i.Severity == IssueSeverity.Error) ? 1 : 0;
}

static int Compare(string a, string b)
{
    var differences = WorldComparer.Compare(Load(a), Load(b));
    foreach (var d in differences) Console.WriteLine("DIFF   " + d);
    Console.WriteLine(differences.Count == 0 ? "OK     identical" : $"FAIL   {differences.Count} difference(s)");
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
