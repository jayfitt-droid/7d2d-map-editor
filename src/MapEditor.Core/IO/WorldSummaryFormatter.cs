using System.Text;
using MapEditor.Core.Model;

namespace MapEditor.Core.IO;

/// <summary>Human-readable overview of a loaded world, shared by the CLI and the app.</summary>
public static class WorldSummaryFormatter
{
    public static string Format(World world)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"World      {world.Name}");
        sb.AppendLine($"Size       {world.Size} x {world.Size} m");

        ushort min = ushort.MaxValue, max = 0;
        foreach (ushort h in world.Height.Data) { if (h < min) min = h; if (h > max) max = h; }
        sb.AppendLine($"Height     {World.HeightToMetres(min):0.0} .. {World.HeightToMetres(max):0.0} m");

        if (world.Biomes is { } b)
        {
            var counts = new long[b.Palette.Count];
            foreach (byte i in b.Indices.Data) counts[i]++;
            sb.AppendLine($"Biomes     {b.Indices.Width} x {b.Indices.Height}, {b.Palette.Count} colours");
            for (int i = 0; i < b.Palette.Count; i++)
                sb.AppendLine($"           #{b.Palette[i]:X6}  {100.0 * counts[i] / b.Indices.Data.Length,5:0.0}%");
        }

        if (world.Roads is { } r)
        {
            var counts = new long[4];
            foreach (byte v in r.Data) if (v < 4) counts[v]++;
            sb.AppendLine($"Roads      asphalt {counts[(int)RoadType.Asphalt]:N0} px, gravel {counts[(int)RoadType.Gravel]:N0} px, river {counts[(int)RoadType.Water]:N0} px");
        }

        if (world.Water is { } w)
            sb.AppendLine($"Water      {w.Data.Count(v => v > 0):N0} px");
        if (world.Radiation is { } rad)
            sb.AppendLine($"Radiation  {rad.Width} x {rad.Height}, {rad.Data.Count(v => v > 0):N0} px");

        sb.AppendLine($"Prefabs    {world.Prefabs.Count:N0} ({world.Prefabs.Count(p => p.Name.StartsWith("rwg_tile_", StringComparison.Ordinal)):N0} street tiles)");
        sb.AppendLine($"Spawns     {world.Spawns.Count}");
        sb.AppendLine($"Extra      {string.Join(", ", world.ExtraFiles.Keys.Order())}");

        foreach (var warning in world.Warnings) sb.AppendLine($"Warning    {warning}");
        return sb.ToString();
    }
}
