using System.Xml.Linq;

namespace MapEditor.Core.Model;

/// <summary>Road surface codes stored in <see cref="World.Roads"/>; mapped to splat3 colours on export.</summary>
public enum RoadType : byte
{
    None = 0,
    Gravel = 1,
    Asphalt = 2,
    /// <summary>RWG's river-path id; written as pure blue in splat3.</summary>
    Water = 3,
}

public readonly record struct Int3(int X, int Y, int Z)
{
    public override string ToString() => $"{X},{Y},{Z}";
}

/// <summary>One &lt;decoration&gt; from prefabs.xml. Position is the bounding-box min corner in world coords.</summary>
public sealed class PrefabInstance
{
    public required string Name { get; set; }
    public Int3 Position { get; set; }
    public int Rotation { get; set; }
    public string Type { get; set; } = "model";
    /// <summary>Attributes we do not interpret, kept so they survive a save.</summary>
    public List<KeyValuePair<string, string>> ExtraAttributes { get; } = [];

    public PrefabInstance Clone()
    {
        var copy = new PrefabInstance { Name = Name, Position = Position, Rotation = Rotation, Type = Type };
        copy.ExtraAttributes.AddRange(ExtraAttributes);
        return copy;
    }
}

/// <summary>One &lt;spawnpoint&gt; from spawnpoints.xml, in world coords.</summary>
public sealed class SpawnPoint
{
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float RotationY { get; set; }

    public SpawnPoint Clone() => (SpawnPoint)MemberwiseClone();
}

/// <summary>Top-down RGBA8 pixels, exactly as decoded from a PNG.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);

/// <summary>
/// An indexed colour raster (biomes). Indices point into <see cref="Palette"/>, whose entries are
/// packed 0xRRGGBB colours exactly as they appear in biomes.png.
/// </summary>
public sealed class IndexedLayer(Grid<byte> indices, List<uint> palette)
{
    public Grid<byte> Indices { get; } = indices;
    public List<uint> Palette { get; } = palette;
}

/// <summary>An in-memory 7 Days to Die world folder.</summary>
public sealed class World
{
    public required string Name { get; set; }

    /// <summary>World edge length in metres (= heightmap resolution, since Scale is 1).</summary>
    public required int Size { get; init; }

    /// <summary>dtm.raw: height * 257 as uint16.</summary>
    public required Grid<ushort> Height { get; init; }

    /// <summary>biomes.png, usually Size/8 square.</summary>
    public IndexedLayer? Biomes { get; set; }

    /// <summary>splat3.png roads, Size square.</summary>
    public Grid<byte>? Roads { get; set; }

    /// <summary>splat4.png blue channel: water surface height (0 = dry), Size square.</summary>
    public Grid<byte>? Water { get; set; }

    /// <summary>radiation.png red channel, usually Size/32 square.</summary>
    public Grid<byte>? Radiation { get; set; }

    public List<PrefabInstance> Prefabs { get; } = [];
    public List<SpawnPoint> Spawns { get; } = [];

    /// <summary>map_info.xml, kept as XML so unknown properties survive.</summary>
    public XDocument MapInfo { get; set; } = new(new XElement("MapInfo"));

    /// <summary>
    /// Original RGBA (top-down) for images whose pixels do not fit our layer encoding. Written back
    /// verbatim while the matching layer is unchanged, so nothing is lost on a round trip.
    /// Key is the file name, e.g. "splat3.png".
    /// </summary>
    public Dictionary<string, RgbaImage> PassthroughImages { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Files we do not model (main.ttw, water_info.xml, splat1.png...), copied as-is.</summary>
    public Dictionary<string, byte[]> ExtraFiles { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Warnings { get; } = [];

    public static float HeightToMetres(ushort raw) => raw / 257f;

    public static ushort MetresToHeight(float metres) => (ushort)Math.Clamp((int)(metres * 257f), 0, ushort.MaxValue);

    /// <summary>Converts a grid column/row to game world X/Z (the world centre is 0,0).</summary>
    public int GridToWorld(int gridCoord) => gridCoord - Size / 2;

    public int WorldToGrid(int worldCoord) => worldCoord + Size / 2;
}
