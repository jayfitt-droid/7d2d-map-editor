using System.Globalization;
using System.Xml.Linq;

namespace MapEditor.Core.GameData;

/// <summary>A biome and the exact colour that identifies it in biomes.png.</summary>
public sealed record BiomeDefinition(string Name, uint Color);

/// <summary>Biome names and map colours, read from the game's Data/Config/biomes.xml.</summary>
public sealed class BiomeTable
{
    /// <summary>Vanilla colours, used when no game install is configured.</summary>
    public static readonly BiomeTable Vanilla = new(
    [
        new("pine_forest", 0x004000),
        new("burnt_forest", 0xBA00FF),
        new("desert", 0xFFE477),
        new("snow", 0xFFFFFF),
        new("wasteland", 0xFFA800),
        new("underwater", 0x001234),
    ]);

    public BiomeTable(IReadOnlyList<BiomeDefinition> biomes) => Biomes = biomes;

    public IReadOnlyList<BiomeDefinition> Biomes { get; }

    public BiomeDefinition? FindByColor(uint rgb) => Biomes.FirstOrDefault(b => b.Color == rgb);

    public static BiomeTable Load(string biomesXmlPath)
    {
        var list = new List<BiomeDefinition>();
        foreach (var e in XDocument.Load(biomesXmlPath).Descendants("biome"))
        {
            string name = (string?)e.Attribute("name") ?? "";
            string color = ((string?)e.Attribute("biomemapcolor") ?? "").TrimStart('#');
            if (name.Length == 0 || color.Length != 6) continue;
            if (uint.TryParse(color, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
                list.Add(new BiomeDefinition(name, rgb));
        }
        return new BiomeTable(list);
    }

    public static BiomeTable Load(GameInstall install) => Load(Path.Combine(install.ConfigDir, "biomes.xml"));
}
