using MapEditor.Core.GameData;

namespace MapEditor.Core.Model;

/// <summary>What is at one point of the world, for the editor's cursor readout.</summary>
public sealed record PointInfo(
    int WorldX,
    int WorldZ,
    float HeightMetres,
    string? Biome,
    RoadType Road,
    int WaterSurface,
    bool Radiation,
    PrefabInstance? Prefab);

public static class WorldQuery
{
    /// <summary>Describes grid cell (<paramref name="x"/>, <paramref name="z"/>); null if outside the world.</summary>
    public static PointInfo? At(World world, int x, int z, BiomeTable biomes, PrefabLibrary prefabs)
    {
        if (!world.Height.Contains(x, z)) return null;

        string? biome = null;
        if (world.Biomes is { } b)
        {
            int s = Math.Max(1, world.Size / b.Indices.Width);
            uint rgb = b.Palette[b.Indices[x / s, z / s]];
            biome = biomes.FindByColor(rgb)?.Name ?? $"#{rgb:X6}";
        }

        bool radiation = false;
        if (world.Radiation is { } r)
        {
            int s = Math.Max(1, world.Size / r.Width);
            radiation = r[x / s, z / s] > 0;
        }

        int wx = world.GridToWorld(x), wz = world.GridToWorld(z);
        return new PointInfo(
            wx, wz,
            World.HeightToMetres(world.Height[x, z]),
            biome,
            world.Roads is { } roads ? (RoadType)roads[x, z] : RoadType.None,
            world.Water?[x, z] ?? 0,
            radiation,
            PrefabAt(world, wx, wz, prefabs));
    }

    /// <summary>The prefab whose footprint covers world point (wx, wz), preferring the smallest.</summary>
    public static PrefabInstance? PrefabAt(World world, int wx, int wz, PrefabLibrary prefabs)
    {
        PrefabInstance? best = null;
        long bestArea = long.MaxValue;
        foreach (var p in world.Prefabs)
        {
            var (sx, sz) = prefabs.Find(p.Name)?.Footprint(p.Rotation) ?? (1, 1);
            if (wx < p.Position.X || wz < p.Position.Z || wx >= p.Position.X + sx || wz >= p.Position.Z + sz) continue;
            long area = (long)sx * sz;
            if (area < bestArea) { best = p; bestArea = area; }
        }
        return best;
    }
}
