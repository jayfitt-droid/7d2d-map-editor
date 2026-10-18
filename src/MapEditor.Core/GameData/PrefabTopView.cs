using System.Text;
using MapEditor.Core.Model;

namespace MapEditor.Core.GameData;

/// <summary>
/// A prefab seen from above: one pixel per block column, north up (row 0 is the prefab's north edge), as
/// premultiplied BGRA32. Pixels are 0 (transparent) where the prefab leaves the ground as it is.
/// </summary>
public sealed record PrefabTopView(int Width, int Height, uint[] Pixels)
{
    /// <summary>
    /// The view after <paramref name="rotation"/> quarter turns, matching how placed prefabs and their markers turn
    /// (<see cref="PrefabMarker.TurnedOnce"/>: x, z becomes size.z - 1 - z, x).
    /// </summary>
    public PrefabTopView Turned(int rotation)
    {
        var view = this;
        for (int r = 0; r < (rotation & 3); r++)
        {
            // Turn in prefab coordinates (z north), then back to rows from the north.
            int w = view.Width, h = view.Height;
            var turned = new uint[w * h];
            for (int row = 0; row < h; row++)
            {
                int z = h - 1 - row;
                for (int x = 0; x < w; x++)
                {
                    int nx = h - 1 - z, nz = x; // new size is (h, w)
                    turned[(w - 1 - nz) * h + nx] = view.Pixels[row * w + x];
                }
            }
            view = new PrefabTopView(h, w, turned);
        }
        return view;
    }
}

/// <summary>
/// Reads a prefab's block grid (.tts, with block names from .blocks.nim) and draws it from above, so the map can
/// show where a street tile's roads run and where a POI's buildings stand.
/// </summary>
public static class PrefabTopViewReader
{
    // Ground and surface colours (RGB); buildings are coloured by material and lightened with height.
    private const uint Asphalt = 0x3C3C40, RoadPaint = 0xD8D2A8, Pavement = 0x9C9CA0, Gravel = 0xB49A6A,
        Dirt = 0x7A5E40, Grass = 0x5A7A3A, Snow = 0xE6ECF0, Sand = 0xD6C08A, Stone = 0x84827E, Water = 0x3A78B8,
        Brick = 0xA0604C, Wood = 0x8E6E4A, Metal = 0x7E8790, Concrete = 0xB4B0A8, Glass = 0x9CC4D8, Roof = 0x6E5048;

    private enum Kind : byte { Skip, KeepGround, Flat, Building }

    /// <summary>The road surface a top-view pixel shows: asphalt (road paint counts), gravel, or none.</summary>
    public static RoadType SurfaceOf(uint pixel) => (pixel & 0xFFFFFF) switch
    {
        Asphalt or RoadPaint => RoadType.Asphalt,
        Gravel when pixel >> 24 != 0 => RoadType.Gravel,
        _ => RoadType.None,
    };

    /// <summary>The top view of the prefab whose .tts is at <paramref name="ttsPath"/>, or null if it can't be read.</summary>
    public static PrefabTopView? Read(string ttsPath, int yOffset)
    {
        string nim = Path.ChangeExtension(ttsPath, null) + ".blocks.nim";
        if (!File.Exists(ttsPath) || !File.Exists(nim)) return null;
        try
        {
            var names = ReadNames(nim);
            using var stream = File.OpenRead(ttsPath);
            using var reader = new BinaryReader(stream);
            if (reader.ReadUInt32() != 0x00737474) return null; // "tts\0"
            uint version = reader.ReadUInt32();
            if (version < 13) return null;
            int sx = reader.ReadInt16(), sy = reader.ReadInt16(), sz = reader.ReadInt16();
            if (sx <= 0 || sy <= 0 || sz <= 0) return null;
            int count = sx * sy * sz;
            var raw = reader.ReadBytes(count * 4);
            if (raw.Length < count * 4) return null;
            // Version 18 widened the block type from 15 to 16 bits.
            uint typeMask = version >= 18 ? 0xFFFFu : 0x7FFFu;
            return Draw(raw, sx, sy, sz, typeMask, names, -yOffset);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or EndOfStreamException or DecoderFallbackException)
        {
            return null;
        }
    }

    /// <summary>The block name map: an int version, a count, then (int id, string name) pairs.</summary>
    private static Dictionary<int, string> ReadNames(string path)
    {
        using var reader = new BinaryReader(File.OpenRead(path), Encoding.UTF8);
        reader.ReadInt32();
        int count = reader.ReadInt32();
        var names = new Dictionary<int, string>(count);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            names[id] = reader.ReadString();
        }
        return names;
    }

    private static PrefabTopView Draw(byte[] raw, int sx, int sy, int sz, uint typeMask, Dictionary<int, string> names, int groundY)
    {
        var kinds = new Dictionary<int, (Kind Kind, uint Colour)>();
        (Kind, uint) Lookup(int type)
        {
            if (!kinds.TryGetValue(type, out var k))
                kinds[type] = k = names.TryGetValue(type, out var name) ? Classify(name) : (Kind.Skip, 0);
            return k;
        }

        var top = new int[sx * sz];
        var colours = new uint[sx * sz];
        var kindsAt = new Kind[sx * sz];
        int layer = sx * sy;
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                top[z * sx + x] = -1;
                // Blocks are stored x fastest, then y, then z; look down each column for the first block that shows.
                for (int y = sy - 1; y >= 0; y--)
                {
                    int i = (x + y * sx + z * layer) * 4;
                    uint value = (uint)(raw[i] | raw[i + 1] << 8 | raw[i + 2] << 16 | raw[i + 3] << 24);
                    int type = (int)(value & typeMask);
                    if (type == 0) continue;
                    var (kind, colour) = Lookup(type);
                    if (kind == Kind.Skip) continue;
                    top[z * sx + x] = y;
                    colours[z * sx + x] = colour;
                    kindsAt[z * sx + x] = kind;
                    break;
                }
            }

        var pixels = new uint[sx * sz];
        for (int z = 0; z < sz; z++)
            for (int x = 0; x < sx; x++)
            {
                int i = z * sx + x;
                if (top[i] < 0 || kindsAt[i] == Kind.KeepGround) continue;
                uint rgb = colours[i];
                float light = 1;
                if (kindsAt[i] == Kind.Building)
                {
                    // Taller is lighter, and a drop to a lower neighbour draws a dark edge, so buildings read as blocks.
                    light = 0.8f + 0.2f * Math.Clamp((top[i] - groundY) / 12f, 0, 1);
                    int lowest = top[i];
                    if (x > 0) lowest = Math.Min(lowest, top[i - 1]);
                    if (x < sx - 1) lowest = Math.Min(lowest, top[i + 1]);
                    if (z > 0) lowest = Math.Min(lowest, top[i - sx]);
                    if (z < sz - 1) lowest = Math.Min(lowest, top[i + sx]);
                    if (top[i] - lowest >= 2) light *= 0.6f;
                }
                uint r = (uint)Math.Min(255, ((rgb >> 16) & 0xFF) * light);
                uint g = (uint)Math.Min(255, ((rgb >> 8) & 0xFF) * light);
                uint b = (uint)Math.Min(255, (rgb & 0xFF) * light);
                pixels[(sz - 1 - z) * sx + x] = 0xFF000000 | r << 16 | g << 8 | b;
            }
        return new PrefabTopView(sx, sz, pixels);
    }

    // Small props, plants, vehicles, loot and spawners: looked through, so the street or floor under them shows.
    private static readonly string[] SkipWords =
    [
        "helper", "plant", "grass", "bush", "shrub", "flower", "crop", "sleeper", "gore", "sign", "poster",
        "light", "lamp", "utility", "cnt", "garbage", "decor", "trash", "pallet", "rubble", "bench", "hydrant",
        "manhole", "barrel", "mattress", "rock", "bollard", "parkingblock", "guardrail", "fence", "chainlink",
        "pole", "pillar", "pipe", "wire", "cable", "spawn", "trigger", "sedan", "truck", "carwreck", "bike", "invisible",
    ];

    private static (Kind, uint) Classify(string name)
    {
        string n = name.ToLowerInvariant();
        if (n == "air") return (Kind.Skip, 0);
        if (n.StartsWith("terrainfiller")) return (Kind.KeepGround, 0);
        if (n.Contains("roadpaint")) return (Kind.Flat, RoadPaint);
        if (n.Contains("asphalt")) return (Kind.Flat, Asphalt);
        if (n.StartsWith("water")) return (Kind.Flat, Water);
        if (n.StartsWith("terr"))
            return (Kind.Flat, n switch
            {
                _ when n.Contains("gravel") => Gravel,
                _ when n.Contains("snow") || n.Contains("ice") => Snow,
                _ when n.Contains("sand") || n.Contains("desert") => Sand,
                _ when n.Contains("grass") || n.Contains("forest") => Grass,
                _ when n.Contains("stone") || n.Contains("rock") || n.Contains("concrete") => Stone,
                _ => Dirt,
            });
        if (n.StartsWith("tree") || SkipWords.Any(n.Contains)) return (Kind.Skip, 0);
        if (n.Contains("sidewalk") || n.Contains("plate") && n.StartsWith("concrete")) return (Kind.Flat, Pavement);
        return (Kind.Building, n switch
        {
            _ when n.Contains("roof") || n.Contains("shingle") => Roof,
            _ when n.Contains("glass") || n.Contains("window") => Glass,
            _ when n.Contains("brick") => Brick,
            _ when n.Contains("wood") || n.Contains("plywood") => Wood,
            _ when n.Contains("metal") || n.Contains("steel") || n.Contains("iron") => Metal,
            _ => Concrete,
        });
    }
}
