using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Loads a game world folder (RWG, pregen or hand-made) into a <see cref="World"/>.</summary>
public static class WorldReader
{
    public static World Read(string folder)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
        string P(string file) => Path.Combine(folder, file);

        if (!File.Exists(P(WorldFiles.Height)))
            throw new FileNotFoundException("The world has no dtm.raw heightmap.", P(WorldFiles.Height));

        var height = HeightRaw.Read(P(WorldFiles.Height));
        var world = new World
        {
            Name = new DirectoryInfo(folder).Name,
            Size = height.Width,
            Height = height,
        };

        if (File.Exists(P(WorldFiles.MapInfo)))
        {
            world.MapInfo = WorldXml.ReadMapInfo(P(WorldFiles.MapInfo));
            string? declared = WorldXml.GetProperty(world.MapInfo, "HeightMapSize");
            if (declared is not null && declared.Replace(" ", "") != $"{world.Size},{world.Size}")
                world.Warnings.Add($"map_info.xml says HeightMapSize={declared} but dtm.raw is {world.Size}x{world.Size}.");
        }
        else
        {
            world.Warnings.Add("map_info.xml is missing; a default one will be written.");
        }

        if (File.Exists(P(WorldFiles.Biomes)))
        {
            var rgba = Png.ReadRgba(P(WorldFiles.Biomes), out int w, out int h);
            world.Biomes = LayerImages.DecodeBiomes(rgba, w, h);
            if (world.Biomes is null)
            {
                world.PassthroughImages[WorldFiles.Biomes] = new RgbaImage(w, h, rgba);
                world.Warnings.Add("biomes.png has more than 255 colours; it is kept as-is and cannot be painted.");
            }
        }
        else world.Warnings.Add("biomes.png is missing.");

        if (File.Exists(P(WorldFiles.Roads)))
        {
            var rgba = Png.ReadRgba(P(WorldFiles.Roads), out int w, out int h);
            RequireWorldSized(world, WorldFiles.Roads, w, h);
            world.Roads = LayerImages.DecodeRoads(rgba, w, h, out bool canonical);
            if (!canonical)
            {
                world.PassthroughImages[WorldFiles.Roads] = new RgbaImage(w, h, rgba);
                world.Warnings.Add("splat3.png uses colours beyond RWG's road set; the original is kept until roads are edited.");
            }
        }
        else world.Warnings.Add("splat3.png is missing (no roads).");

        if (File.Exists(P(WorldFiles.Water)))
        {
            var rgba = Png.ReadRgba(P(WorldFiles.Water), out int w, out int h);
            RequireWorldSized(world, WorldFiles.Water, w, h);
            world.Water = LayerImages.DecodeWater(rgba, w, h, out bool canonical);
            if (!canonical)
            {
                world.PassthroughImages[WorldFiles.Water] = new RgbaImage(w, h, rgba);
                world.Warnings.Add("splat4.png has data outside the blue channel; the original is kept until water is edited.");
            }
        }
        else if (File.Exists(P("splat4_processed.png")))
            world.Warnings.Add("splat4.png is missing but splat4_processed.png exists; water from the processed cache is not imported.");

        if (File.Exists(P(WorldFiles.Radiation)))
        {
            var rgba = Png.ReadRgba(P(WorldFiles.Radiation), out int w, out int h);
            world.Radiation = LayerImages.DecodeRadiation(rgba, w, h);
        }

        if (File.Exists(P(WorldFiles.Prefabs))) world.Prefabs.AddRange(WorldXml.ReadPrefabs(P(WorldFiles.Prefabs)));
        if (File.Exists(P(WorldFiles.Spawns))) world.Spawns.AddRange(WorldXml.ReadSpawns(P(WorldFiles.Spawns)));

        foreach (var file in Directory.EnumerateFiles(folder))
        {
            string name = Path.GetFileName(file);
            if (WorldFiles.Modelled.Contains(name) || WorldFiles.IsDerived(name)) continue;
            world.ExtraFiles[name] = File.ReadAllBytes(file);
        }
        foreach (var dir in Directory.EnumerateDirectories(folder))
            world.Warnings.Add($"Sub-folder '{Path.GetFileName(dir)}' is ignored.");

        if (!world.ExtraFiles.ContainsKey(WorldFiles.Level))
            world.Warnings.Add("main.ttw is missing; the game cannot load the world without one.");

        return world;
    }

    private static void RequireWorldSized(World world, string file, int w, int h)
    {
        if (w != world.Size || h != world.Size)
            throw new InvalidDataException($"{file} is {w}x{h} but the heightmap is {world.Size}x{world.Size}.");
    }
}
