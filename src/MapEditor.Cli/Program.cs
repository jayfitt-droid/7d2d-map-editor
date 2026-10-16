using System.Diagnostics;
using MapEditor.Core.GameData;
using MapEditor.Core.IO;
using MapEditor.Core.Model;

return args switch
{
    ["info", var world] => Info(world),
    ["roundtrip", var world, var output, .. var rest] => RoundTrip(world, output, overwrite: rest.Contains("--overwrite")),
    ["installs"] => Installs(),
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
        """);
    return 2;
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
