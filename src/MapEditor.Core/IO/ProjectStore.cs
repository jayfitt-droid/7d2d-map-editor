using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>
/// The editor's own project file (.7dmap): a zip holding project.json (name, size, prefabs, spawns, map_info and
/// settings) plus each layer as raw little-endian samples. Unlike a game world folder it keeps everything the
/// editor knows, including the export folder, and it saves faster because nothing is PNG-encoded.
/// </summary>
public static class ProjectStore
{
    public const string Extension = ".7dmap";
    public const int FormatVersion = 1;

    private const string ManifestEntry = "project.json";
    private const string HeightEntry = "layers/height.u16";
    private const string BiomesEntry = "layers/biomes.u8";
    private const string RoadsEntry = "layers/roads.u8";
    private const string WaterEntry = "layers/water.u8";
    private const string RadiationEntry = "layers/radiation.u8";
    private const string PassthroughDir = "passthrough/";
    private const string ExtraDir = "extra/";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Editor state stored beside the world.</summary>
    public sealed class ProjectSettings
    {
        /// <summary>The game world folder this project was last exported to.</summary>
        public string? ExportFolder { get; set; }
    }

    private sealed record Size2(int Width, int Height);

    private sealed record PrefabDto(string Name, string Type, int X, int Y, int Z, int Rotation, List<string[]>? Extra);

    private sealed record SpawnDto(float X, float Y, float Z, float RotationY);

    private sealed record PassthroughDto(string File, int Width, int Height);

    private sealed class Manifest
    {
        public int Format { get; set; }
        public string Name { get; set; } = "";
        public int Size { get; set; }
        public string MapInfo { get; set; } = "<MapInfo />";
        public List<uint>? BiomePalette { get; set; }
        public Size2? Biomes { get; set; }
        public bool Roads { get; set; }
        public bool Water { get; set; }
        public Size2? Radiation { get; set; }
        public List<PrefabDto> Prefabs { get; set; } = [];
        public List<SpawnDto> Spawns { get; set; } = [];
        public List<PassthroughDto> Passthrough { get; set; } = [];
        public List<string> ExtraFiles { get; set; } = [];
        public ProjectSettings Settings { get; set; } = new();
    }

    /// <summary>
    /// Writes the project. The file is written beside the target first and then moved over it, so a failed
    /// save never leaves a half-written project behind.
    /// </summary>
    public static void Save(World world, string path, ProjectSettings? settings = null)
    {
        var manifest = new Manifest
        {
            Format = FormatVersion,
            Name = world.Name,
            Size = world.Size,
            MapInfo = world.MapInfo.ToString(SaveOptions.DisableFormatting),
            BiomePalette = world.Biomes?.Palette,
            Biomes = world.Biomes is { } b ? new Size2(b.Indices.Width, b.Indices.Height) : null,
            Roads = world.Roads is not null,
            Water = world.Water is not null,
            Radiation = world.Radiation is { } r ? new Size2(r.Width, r.Height) : null,
            Prefabs = world.Prefabs.Select(p => new PrefabDto(p.Name, p.Type, p.Position.X, p.Position.Y, p.Position.Z, p.Rotation,
                p.ExtraAttributes.Count == 0 ? null : p.ExtraAttributes.Select(a => new[] { a.Key, a.Value }).ToList())).ToList(),
            Spawns = world.Spawns.Select(s => new SpawnDto(s.X, s.Y, s.Z, s.RotationY)).ToList(),
            ExtraFiles = world.ExtraFiles.Keys.ToList(),
            Settings = settings ?? new ProjectSettings(),
        };

        // A passthrough image is only used while its layer is unedited (see WorldWriter), so an edited layer's
        // original is dropped rather than carried along.
        var passthrough = world.PassthroughImages.Where(kv => kv.Key switch
        {
            WorldFiles.Roads => world.Roads is not { Version: > 0 },
            WorldFiles.Water => world.Water is not { Version: > 0 },
            WorldFiles.Biomes => world.Biomes is null,
            _ => true,
        }).ToList();
        manifest.Passthrough = passthrough.Select(kv => new PassthroughDto(kv.Key, kv.Value.Width, kv.Value.Height)).ToList();

        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = full + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using (var entry = zip.CreateEntry(ManifestEntry, CompressionLevel.Optimal).Open())
                JsonSerializer.Serialize(entry, manifest, Json);
            WriteGrid(zip, HeightEntry, world.Height);
            if (world.Biomes is { } biomes) WriteGrid(zip, BiomesEntry, biomes.Indices);
            if (world.Roads is { } roads) WriteGrid(zip, RoadsEntry, roads);
            if (world.Water is { } water) WriteGrid(zip, WaterEntry, water);
            if (world.Radiation is { } radiation) WriteGrid(zip, RadiationEntry, radiation);
            foreach (var (file, image) in passthrough) WriteBytes(zip, PassthroughDir + file, image.Pixels);
            foreach (var (file, bytes) in world.ExtraFiles) WriteBytes(zip, ExtraDir + file, bytes);
        }
        File.Move(temp, full, overwrite: true);
    }

    public static World Load(string path) => Load(path, out _);

    public static World Load(string path, out ProjectSettings settings)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifestEntry = zip.GetEntry(ManifestEntry) ?? throw new InvalidDataException("The file is not a map editor project (no project.json).");
        Manifest manifest;
        using (var s = manifestEntry.Open())
            manifest = JsonSerializer.Deserialize<Manifest>(s, Json) ?? throw new InvalidDataException("project.json is empty.");
        if (manifest.Format > FormatVersion)
            throw new InvalidDataException($"The project was saved by a newer version of the editor (format {manifest.Format}).");
        if (manifest.Size <= 0) throw new InvalidDataException("project.json has no world size.");

        int size = manifest.Size;
        var world = new World
        {
            Name = manifest.Name,
            Size = size,
            Height = ReadGrid<ushort>(zip, HeightEntry, size, size) ?? throw new InvalidDataException("The project has no heightmap."),
            MapInfo = XDocument.Parse(manifest.MapInfo),
        };
        if (manifest.Biomes is { } bs && ReadGrid<byte>(zip, BiomesEntry, bs.Width, bs.Height) is { } biomes)
            world.Biomes = new IndexedLayer(biomes, manifest.BiomePalette ?? []);
        if (manifest.Roads) world.Roads = ReadGrid<byte>(zip, RoadsEntry, size, size);
        if (manifest.Water) world.Water = ReadGrid<byte>(zip, WaterEntry, size, size);
        if (manifest.Radiation is { } rs) world.Radiation = ReadGrid<byte>(zip, RadiationEntry, rs.Width, rs.Height);

        foreach (var p in manifest.Prefabs)
        {
            var prefab = new PrefabInstance { Name = p.Name, Type = p.Type, Position = new Int3(p.X, p.Y, p.Z), Rotation = p.Rotation };
            foreach (var pair in p.Extra ?? []) if (pair.Length == 2) prefab.ExtraAttributes.Add(new(pair[0], pair[1]));
            world.Prefabs.Add(prefab);
        }
        foreach (var s in manifest.Spawns)
            world.Spawns.Add(new SpawnPoint { X = s.X, Y = s.Y, Z = s.Z, RotationY = s.RotationY });
        foreach (var p in manifest.Passthrough)
            if (ReadBytes(zip, PassthroughDir + p.File) is { } pixels && pixels.Length == p.Width * p.Height * 4)
                world.PassthroughImages[p.File] = new RgbaImage(p.Width, p.Height, pixels);
        foreach (var file in manifest.ExtraFiles)
            if (ReadBytes(zip, ExtraDir + file) is { } bytes) world.ExtraFiles[file] = bytes;

        settings = manifest.Settings ?? new ProjectSettings();
        return world;
    }

    private static void WriteGrid<T>(ZipArchive zip, string name, Grid<T> grid) where T : unmanaged
    {
        // Fastest still shrinks typical layers 5-50x; Optimal costs seconds on an 8k heightmap.
        using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        entry.Write(MemoryMarshal.AsBytes(grid.Data.AsSpan()));
    }

    private static void WriteBytes(ZipArchive zip, string name, byte[] bytes)
    {
        using var entry = zip.CreateEntry(name, CompressionLevel.Fastest).Open();
        entry.Write(bytes);
    }

    private static Grid<T>? ReadGrid<T>(ZipArchive zip, string name, int width, int height) where T : unmanaged
    {
        if (zip.GetEntry(name) is not { } entry) return null;
        var grid = new Grid<T>(width, height);
        var bytes = MemoryMarshal.AsBytes(grid.Data.AsSpan());
        using var s = entry.Open();
        s.ReadExactly(bytes);
        return grid;
    }

    private static byte[]? ReadBytes(ZipArchive zip, string name)
    {
        if (zip.GetEntry(name) is not { } entry) return null;
        using var s = entry.Open();
        using var ms = new MemoryStream((int)entry.Length);
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
