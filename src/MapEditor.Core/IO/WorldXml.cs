using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Readers and writers for prefabs.xml, spawnpoints.xml and map_info.xml.</summary>
public static class WorldXml
{
    private static readonly XmlWriterSettings WriterSettings = new()
    {
        Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        Indent = true,
        IndentChars = "  ",
    };

    public static List<PrefabInstance> ReadPrefabs(string path)
    {
        var result = new List<PrefabInstance>();
        foreach (var e in XDocument.Load(path).Root?.Elements("decoration") ?? [])
        {
            var prefab = new PrefabInstance { Name = (string?)e.Attribute("name") ?? "" };
            foreach (var attr in e.Attributes())
            {
                switch (attr.Name.LocalName)
                {
                    case "name": break;
                    case "type": prefab.Type = attr.Value; break;
                    case "position": prefab.Position = ParseInt3(attr.Value); break;
                    case "rotation": prefab.Rotation = int.Parse(attr.Value, CultureInfo.InvariantCulture); break;
                    default: prefab.ExtraAttributes.Add(new(attr.Name.LocalName, attr.Value)); break;
                }
            }
            result.Add(prefab);
        }
        return result;
    }

    public static void WritePrefabs(string path, IEnumerable<PrefabInstance> prefabs)
    {
        using var w = XmlWriter.Create(path, WriterSettings);
        w.WriteStartDocument();
        w.WriteStartElement("prefabs");
        foreach (var p in prefabs)
        {
            w.WriteStartElement("decoration");
            w.WriteAttributeString("type", p.Type);
            w.WriteAttributeString("name", p.Name);
            w.WriteAttributeString("position", p.Position.ToString());
            w.WriteAttributeString("rotation", p.Rotation.ToString(CultureInfo.InvariantCulture));
            foreach (var (k, v) in p.ExtraAttributes) w.WriteAttributeString(k, v);
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    public static List<SpawnPoint> ReadSpawns(string path)
    {
        var result = new List<SpawnPoint>();
        foreach (var e in XDocument.Load(path).Root?.Elements("spawnpoint") ?? [])
        {
            var pos = ParseFloats((string?)e.Attribute("position"), 3);
            var rot = ParseFloats((string?)e.Attribute("rotation"), 3);
            result.Add(new SpawnPoint { X = pos[0], Y = pos[1], Z = pos[2], RotationY = rot[1] });
        }
        return result;
    }

    public static void WriteSpawns(string path, IEnumerable<SpawnPoint> spawns)
    {
        using var w = XmlWriter.Create(path, WriterSettings);
        w.WriteStartDocument();
        w.WriteStartElement("spawnpoints");
        foreach (var s in spawns)
        {
            w.WriteStartElement("spawnpoint");
            w.WriteAttributeString("position", $"{F(s.X)},{F(s.Y)},{F(s.Z)}");
            w.WriteAttributeString("rotation", $"0,{F(s.RotationY)},0");
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    public static XDocument ReadMapInfo(string path) => XDocument.Load(path);

    public static void WriteMapInfo(string path, XDocument mapInfo, int worldSize)
    {
        var doc = new XDocument(mapInfo);
        if (doc.Root is null) doc.Add(new XElement("MapInfo"));
        SetProperty(doc, "HeightMapSize", $"{worldSize},{worldSize}");
        if (GetProperty(doc, "Scale") is null) SetProperty(doc, "Scale", "1");
        using var w = XmlWriter.Create(path, WriterSettings);
        doc.Save(w);
    }

    public static string? GetProperty(XDocument doc, string name) =>
        doc.Root?.Elements("property").FirstOrDefault(p => (string?)p.Attribute("name") == name)?.Attribute("value")?.Value;

    public static void SetProperty(XDocument doc, string name, string value)
    {
        var root = doc.Root ?? throw new InvalidOperationException("map_info has no root element.");
        var existing = root.Elements("property").FirstOrDefault(p => (string?)p.Attribute("name") == name);
        if (existing is not null) existing.SetAttributeValue("value", value);
        else root.Add(new XElement("property", new XAttribute("name", name), new XAttribute("value", value)));
    }

    private static Int3 ParseInt3(string text)
    {
        var f = ParseFloats(text, 3);
        return new Int3((int)MathF.Round(f[0]), (int)MathF.Round(f[1]), (int)MathF.Round(f[2]));
    }

    private static float[] ParseFloats(string? text, int count)
    {
        var result = new float[count];
        if (string.IsNullOrWhiteSpace(text)) return result;
        var parts = text.Split(',');
        for (int i = 0; i < count && i < parts.Length; i++)
            result[i] = float.Parse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);
        return result;
    }

    private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
}
